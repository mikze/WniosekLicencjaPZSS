import type { Start } from './api'

// Logika zakładki „Porównanie” — czyste funkcje, bez Reacta.

/** Wynik jako liczba ("93,9" → 93.9, "577-28x" → 577). null = brak / DNS. */
export function parseScore(result: string | null): number | null {
  if (!result) return null
  const m = /^\s*(\d+(?:[.,]\d+)?)/.exec(result)
  return m ? Number(m[1].replace(',', '.')) : null
}

export function parsePlace(place: string | null): number | null {
  if (!place) return null
  const n = Number(place.replace('.', ''))
  return Number.isInteger(n) && n > 0 ? n : null
}

/** Jedna konkurencja na jednych zawodach (jeden protokół w jednym PDF-ie). */
export interface CompetitionRow {
  key: string
  competition: string
  /** Wynik każdego zawodnika (klucz: zapytanie), brak wpisu = nie startował. */
  entries: Map<string, Start>
  /** Zawodnicy z najlepszym wynikiem w tej konkurencji (może być remis). */
  best: Set<string>
}

export interface EventBlock {
  key: string
  date: string | null
  eventName: string
  pdfUrl: string
  rows: CompetitionRow[]
}

export interface AthleteSummary {
  name: string
  starts: number
  events: number
  sharedStarts: number
  wins: number
  losses: number
  avgPlace: number | null
  bestPlace: number | null
  /** Różne zapisy nazwiska w protokołach — więcej niż jeden = zapytanie pasuje do kilku osób. */
  athletes: string[]
}

export interface BestByCompetition {
  competition: string
  /** Najlepszy start zawodnika i liczba startów w tej konkurencji. */
  perAthlete: Map<string, { best: Start; count: number }>
  leader: Set<string>
}

export interface Comparison {
  names: string[]
  events: EventBlock[]
  summaries: AthleteSummary[]
  /** headToHead[a][b] = ile razy a wyprzedził b. */
  headToHead: Record<string, Record<string, number>>
  bestByCompetition: BestByCompetition[]
}

/**
 * Kto wypadł lepiej w tej samej konkurencji: miejsce (niższe lepsze), a gdy któregoś brak — wynik (wyższy lepszy).
 * Zwraca >0 gdy lepszy jest a, <0 gdy b, 0 gdy remis lub nie da się porównać.
 */
export function compareStarts(a: Start, b: Start): number {
  const pa = parsePlace(a.place)
  const pb = parsePlace(b.place)
  if (pa !== null && pb !== null) return pb - pa
  const sa = parseScore(a.result)
  const sb = parseScore(b.result)
  if (sa !== null && sb !== null) return sa - sb
  return 0
}

const startedFor = (s: Start) => parseScore(s.result) !== null || parsePlace(s.place) !== null

const normalizeName = (s: string) =>
  s
    .normalize('NFD')
    .replace(/\p{M}/gu, '')
    .replace(/ł/gi, 'l')
    .toUpperCase()
    .replace(/[^A-Z]+/g, ' ')
    .trim()
    .split(' ')
    .sort()
    .join(' ')

/** Nazwa konkurencji do zestawienia rekordów: bez wielkości liter i zbędnych spacji. */
const competitionKey = (c: string) => c.toLowerCase().replace(/\s+/g, ' ').trim()

export function buildComparison(results: Start[], names: string[]): Comparison {
  // Konkurencje po kolei, tak jak w komunikatach.
  const eventMap = new Map<string, EventBlock>()
  const rowMap = new Map<string, CompetitionRow>()
  for (const s of results) {
    let ev = eventMap.get(s.pdfUrl)
    if (!ev) {
      ev = { key: s.pdfUrl, date: s.date, eventName: s.eventName, pdfUrl: s.pdfUrl, rows: [] }
      eventMap.set(s.pdfUrl, ev)
    }
    const rowKey = `${s.pdfUrl}|${s.competition}`
    let row = rowMap.get(rowKey)
    if (!row) {
      row = { key: rowKey, competition: s.competition, entries: new Map(), best: new Set() }
      rowMap.set(rowKey, row)
      ev.rows.push(row)
    }
    const prev = row.entries.get(s.query)
    if (!prev || compareStarts(s, prev) > 0) row.entries.set(s.query, s)
  }

  for (const row of rowMap.values()) {
    const started = [...row.entries.values()].filter(startedFor)
    if (started.length < 2) continue
    const top = started.reduce((a, b) => (compareStarts(a, b) >= 0 ? a : b))
    row.best = new Set(started.filter((s) => compareStarts(s, top) === 0).map((s) => s.query))
  }

  // Pojedynki: każda para zawodników w każdej wspólnej konkurencji.
  const headToHead: Record<string, Record<string, number>> = {}
  for (const a of names) headToHead[a] = Object.fromEntries(names.map((b) => [b, 0]))
  for (const row of rowMap.values()) {
    for (const [a, sa] of row.entries)
      for (const [b, sb] of row.entries) {
        if (a === b || !headToHead[a]) continue
        if (startedFor(sa) && startedFor(sb) && compareStarts(sa, sb) > 0) headToHead[a][b]++
      }
  }

  const summaries: AthleteSummary[] = names.map((name) => {
    const mine = results.filter((r) => r.query === name)
    const places = mine.map((s) => parsePlace(s.place)).filter((p): p is number => p !== null)
    const shared = [...rowMap.values()].filter((row) => row.entries.has(name) && row.entries.size > 1).length
    const athletes = new Map<string, string>()
    for (const s of mine) if (!athletes.has(normalizeName(s.athlete))) athletes.set(normalizeName(s.athlete), s.athlete)
    return {
      name,
      starts: mine.length,
      events: new Set(mine.map((s) => s.pdfUrl)).size,
      sharedStarts: shared,
      wins: names.reduce((acc, other) => acc + (headToHead[name]?.[other] ?? 0), 0),
      losses: names.reduce((acc, other) => acc + (headToHead[other]?.[name] ?? 0), 0),
      avgPlace: places.length ? places.reduce((a, b) => a + b, 0) / places.length : null,
      bestPlace: places.length ? Math.min(...places) : null,
      athletes: [...athletes.values()],
    }
  })

  // Rekordy: najlepszy wynik każdego zawodnika w danej konkurencji (po nazwie konkurencji).
  const bestMap = new Map<string, BestByCompetition>()
  for (const s of results) {
    if (parseScore(s.result) === null) continue
    const key = competitionKey(s.competition)
    let entry = bestMap.get(key)
    if (!entry) {
      entry = { competition: s.competition, perAthlete: new Map(), leader: new Set() }
      bestMap.set(key, entry)
    }
    const cur = entry.perAthlete.get(s.query)
    if (!cur) entry.perAthlete.set(s.query, { best: s, count: 1 })
    else {
      cur.count++
      // Rekord to najwyższy wynik — miejsca z różnych zawodów nie są porównywalne.
      if ((parseScore(s.result) ?? 0) > (parseScore(cur.best.result) ?? 0)) cur.best = s
    }
  }
  for (const entry of bestMap.values()) {
    const scores = [...entry.perAthlete.entries()].map(([n, v]) => [n, parseScore(v.best.result) ?? 0] as const)
    if (scores.length < 2) continue
    const max = Math.max(...scores.map(([, v]) => v))
    entry.leader = new Set(scores.filter(([, v]) => v === max).map(([n]) => n))
  }
  const bestByCompetition = [...bestMap.values()].sort(
    (a, b) => b.perAthlete.size - a.perAthlete.size || a.competition.localeCompare(b.competition, 'pl'),
  )

  return { names, events: [...eventMap.values()], summaries, headToHead, bestByCompetition }
}
