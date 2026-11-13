import { useEffect, useMemo, useState } from 'react'
import { getRanking, type AthleteStat, type Ranking } from '../api'
import { ScoreDistribution } from './ScoreDistribution'

interface Props {
  years: number[]
  /** Zakładka jest widoczna — dane wczytujemy dopiero wtedy. */
  active: boolean
  /** Kliknięcie zawodnika: pokaż jego starty i wniosek w pierwszej zakładce. */
  onOpenAthlete: (name: string, year: number) => void
}

type SortKey = 'starts' | 'events' | 'medals' | 'gold' | 'name'

const PAGE = 25
const collator = new Intl.Collator('pl', { sensitivity: 'base' })

export function RankingView({ years, active, onOpenAthlete }: Props) {
  const [chosenYear, setChosenYear] = useState<number | null>(null)
  const year = chosenYear ?? years[0] ?? null
  /** Wybrane kluby; puste = wszystkie. */
  const [clubs, setClubs] = useState<string[]>([])
  const key = JSON.stringify({ year, clubs })
  const [result, setResult] = useState<{ key: string; year: number; data?: Ranking; error?: string } | null>(null)

  useEffect(() => {
    if (!active) return
    const { year: y, clubs: c } = JSON.parse(key) as { year: number | null; clubs: string[] }
    if (y === null) return
    const controller = new AbortController()
    getRanking(y, c, controller.signal)
      .then((data) => setResult({ key, year: y, data }))
      .catch((e: Error) => {
        if (e.name !== 'AbortError') setResult((cur) => ({ key, year: y, data: cur?.data, error: e.message }))
      })
    return () => controller.abort()
  }, [active, key])

  const loading = year !== null && result?.key !== key
  // Przy zmianie filtra pokazujemy poprzednie dane tego samego roku (wyszarzone), zamiast pustej strony.
  const data = result?.year === year ? result.data : undefined
  const availableClubs = data?.availableClubs ?? []

  return (
    <div className="ranking">
      <section className="card intro">
        <p>
          <strong>Ranking i statystyki roku</strong> — wszyscy zawodnicy ze wszystkich komunikatów z wybranego roku:
          najaktywniejsi, podia, kto spełnia warunki licencji PZSS, najlepsze wyniki w konkurencjach, kluby, frekwencja
          i rozkład punktów w konkurencjach.
        </p>
        <div className="ranking-filters">
          <label className="field compact year-select">
            <span className="field-label">Rok</span>
            <select value={year ?? ''} onChange={(e) => setChosenYear(Number(e.target.value))}>
              {years.map((y) => (
                <option key={y} value={y}>
                  {y}
                </option>
              ))}
            </select>
          </label>
          <ClubFilter available={availableClubs} selected={clubs} onChange={setClubs} />
        </div>
      </section>

      {loading && !data && (
        <p className="muted">
          Liczę ranking {year}… Pierwsze wczytanie roku czyta wszystkie komunikaty i może potrwać kilkanaście sekund.
        </p>
      )}
      {result?.error && result.key === key && <p className="error">{result.error}</p>}
      {data && (
        <div className={loading ? 'stale-block' : undefined}>
          <RankingBody data={data} onOpenAthlete={(name) => onOpenAthlete(name, data.year)} />
        </div>
      )}
    </div>
  )
}

/** Filtr klubów: domyślnie wszystkie, można dodać jeden lub kilka konkretnych. */
function ClubFilter({
  available,
  selected,
  onChange,
}: {
  available: string[]
  selected: string[]
  onChange: (clubs: string[]) => void
}) {
  const rest = available.filter((c) => !selected.includes(c))
  return (
    <fieldset className="field compact club-filter">
      <legend className="field-label">Kluby</legend>
      <div className="chips">
        {selected.length === 0 && <span className="chip chip-on">Wszystkie kluby</span>}
        {selected.map((c) => (
          <button
            key={c}
            type="button"
            className="chip chip-on"
            onClick={() => onChange(selected.filter((x) => x !== c))}
            aria-label={`Usuń filtr: ${c}`}
            title="Usuń z filtra"
          >
            {c} ×
          </button>
        ))}
        <select
          value=""
          onChange={(e) => e.target.value && onChange([...selected, e.target.value])}
          aria-label="Dodaj klub do filtra"
          disabled={rest.length === 0}
        >
          <option value="">{selected.length === 0 ? 'Wybierz klub…' : 'Dodaj klub…'}</option>
          {rest.map((c) => (
            <option key={c} value={c}>
              {c}
            </option>
          ))}
        </select>
        {selected.length > 0 && (
          <button type="button" className="link-button" onClick={() => onChange([])}>
            wszystkie
          </button>
        )}
      </div>
    </fieldset>
  )
}

function RankingBody({ data, onOpenAthlete }: { data: Ranking; onOpenAthlete: (name: string) => void }) {
  return (
    <main className="results">
      <section className="tiles" aria-label="Podsumowanie roku">
        <Tile label="Zawody" value={data.events} hint="komunikaty z wynikami" />
        <Tile label="Starty" value={data.starts} />
        <Tile label="Zawodnicy" value={data.athleteCount} />
        <Tile label="Kluby" value={data.clubCount} />
        <Tile
          label="Spełnia warunki licencji"
          value={data.meetingLicense}
          hint={`${Math.round((data.meetingLicense / Math.max(1, data.athleteCount)) * 100)}% zawodników`}
        />
      </section>
      <AthletesTable athletes={data.athletes} onOpenAthlete={onOpenAthlete} />
      <Competitions data={data} onOpenAthlete={onOpenAthlete} />
      <Attendance data={data} />
      <Clubs data={data} />
      <ScoreDistribution competitions={data.competitions} />
    </main>
  )
}

function Tile({ label, value, hint }: { label: string; value: number; hint?: string }) {
  return (
    <div className="tile card">
      <span className="tile-label">{label}</span>
      <strong className="tile-value">{value.toLocaleString('pl-PL')}</strong>
      {hint && <span className="tile-hint">{hint}</span>}
    </div>
  )
}

function AthleteLink({ name, onOpen }: { name: string; onOpen: (name: string) => void }) {
  return (
    <button className="name-link" onClick={() => onOpen(name)} title="Pokaż starty i wniosek PZSS">
      {name}
    </button>
  )
}

function AthletesTable({ athletes, onOpenAthlete }: { athletes: AthleteStat[]; onOpenAthlete: (name: string) => void }) {
  const [query, setQuery] = useState('')
  const [onlyLicensed, setOnlyLicensed] = useState(false)
  const [sort, setSort] = useState<SortKey>('starts')
  const [limit, setLimit] = useState(PAGE)

  const rows = useMemo(() => {
    const q = normalize(query)
    const filtered = athletes.filter(
      (a) => (!onlyLicensed || a.meetsLicense) && (!q || normalize(`${a.name} ${a.club ?? ''}`).includes(q)),
    )
    const medals = (a: AthleteStat) => a.gold + a.silver + a.bronze
    const by: Record<SortKey, (a: AthleteStat, b: AthleteStat) => number> = {
      starts: (a, b) => b.starts - a.starts || medals(b) - medals(a),
      events: (a, b) => b.events - a.events || b.starts - a.starts,
      medals: (a, b) => medals(b) - medals(a) || b.gold - a.gold,
      gold: (a, b) => b.gold - a.gold || b.silver - a.silver || b.bronze - a.bronze,
      name: (a, b) => collator.compare(a.name, b.name),
    }
    // Pozycja liczona po sortowaniu, przed filtrem tekstowym — żeby wyszukany zawodnik miał swoje prawdziwe miejsce.
    const ranked = [...athletes].sort(by[sort])
    const position = new Map(ranked.map((a, i) => [a, i + 1]))
    return filtered.sort(by[sort]).map((a) => ({ a, pos: position.get(a)! }))
  }, [athletes, query, onlyLicensed, sort])

  const header = (key: SortKey, label: string, className = 'num') => (
    <th className={className} aria-sort={sort === key ? 'descending' : undefined}>
      <button className={sort === key ? 'sort active' : 'sort'} onClick={() => setSort(key)}>
        {label}
        {sort === key ? ' ↓' : ''}
      </button>
    </th>
  )

  return (
    <section className="compare-section">
      <div className="section-head">
        <h2>Zawodnicy</h2>
        <div className="table-tools">
          <input
            type="search"
            placeholder="Szukaj zawodnika lub klubu"
            value={query}
            onChange={(e) => {
              setQuery(e.target.value)
              setLimit(PAGE)
            }}
            aria-label="Szukaj zawodnika lub klubu"
          />
          <label className="toggle">
            <input type="checkbox" checked={onlyLicensed} onChange={(e) => setOnlyLicensed(e.target.checked)} />
            <span>Tylko spełniający warunki licencji</span>
          </label>
        </div>
      </div>
      <div className="card table-card table-scroll">
        <table className="ranking-table">
          <thead>
            <tr>
              <th className="num">#</th>
              {header('name', 'Zawodnik', '')}
              <th className="hide-narrow">Klub</th>
              {header('starts', 'Starty')}
              {header('events', 'Zawody')}
              {header('gold', '1.')}
              <th className="num" title="2. miejsca">2.</th>
              <th className="num" title="3. miejsca">3.</th>
              {header('medals', 'Podia')}
              <th className="num hide-narrow" title="Starty w pistolecie / karabinie / strzelbie (liczone jak we wniosku)">
                P / K / S
              </th>
              <th title="Zakres licencji, dla którego zawodnik spełnia warunki PZSS (P = pistolet, K = karabin, S = strzelba)">
                Licencja
              </th>
            </tr>
          </thead>
          <tbody>
            {rows.slice(0, limit).map(({ a, pos }) => (
              <tr key={a.name}>
                <td className="num muted">{pos}</td>
                <td>
                  <AthleteLink name={a.name} onOpen={onOpenAthlete} />
                </td>
                <td className="hide-narrow muted">{a.club ?? ''}</td>
                <td className="num strong">{a.starts}</td>
                <td className="num">{a.events}</td>
                <td className="num">{a.gold || ''}</td>
                <td className="num">{a.silver || ''}</td>
                <td className="num">{a.bronze || ''}</td>
                <td className="num">{a.gold + a.silver + a.bronze || ''}</td>
                <td className="num hide-narrow muted">
                  {a.pistol} / {a.rifle} / {a.shotgun}
                </td>
                <td
                  className={a.meetsLicense ? 'license-ok' : 'muted'}
                  title={a.meetsLicense ? `Spełnia warunki dla licencji: ${a.licenseScope}` : 'Nie spełnia warunków PZSS'}
                >
                  {a.meetsLicense ? `✓ ${shortScope(a.licenseScope)}` : '—'}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <div className="table-footer">
        <span className="muted small">
          {rows.length === 0
            ? 'Brak zawodników spełniających kryteria.'
            : `Pokazano ${Math.min(limit, rows.length)} z ${rows.length}.`}
        </span>
        {rows.length > limit && (
          <button className="link-button" onClick={() => setLimit((l) => l + PAGE * 4)}>
            Pokaż więcej
          </button>
        )}
      </div>
    </section>
  )
}

function Competitions({ data, onOpenAthlete }: { data: Ranking; onOpenAthlete: (name: string) => void }) {
  const [index, setIndex] = useState(0)
  const comp = data.competitions[Math.min(index, data.competitions.length - 1)]
  if (!comp) return null
  return (
    <section className="compare-section">
      <div className="section-head">
        <h2>Najlepsze wyniki w konkurencji</h2>
        <select value={index} onChange={(e) => setIndex(Number(e.target.value))} aria-label="Konkurencja">
          {data.competitions.map((c, i) => (
            <option key={c.name} value={i}>
              {c.name} ({c.starters})
            </option>
          ))}
        </select>
      </div>
      <p className="muted small">
        {comp.starters} zawodników, {comp.starts} startów na {comp.events} zawodach. Najlepszy wynik każdego zawodnika w roku.
      </p>
      <div className="card table-card table-scroll">
        <table className="ranking-table">
          <thead>
            <tr>
              <th className="num">#</th>
              <th>Zawodnik</th>
              <th className="hide-narrow">Klub</th>
              <th className="num">Wynik</th>
              <th>Zawody</th>
            </tr>
          </thead>
          <tbody>
            {comp.top.map((t, i) => (
              <tr key={`${t.athlete}-${i}`}>
                <td className="num muted">{i + 1}.</td>
                <td>
                  <AthleteLink name={t.athlete} onOpen={onOpenAthlete} />
                </td>
                <td className="hide-narrow muted">{t.club ?? ''}</td>
                <td className="num strong">{t.result}</td>
                <td className="muted small">
                  {t.date} · {t.eventName}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  )
}

/** Frekwencja: jedna seria (zawodnicy na zawodach) — poziome słupki z wartością na końcu, bez legendy. */
function Attendance({ data }: { data: Ranking }) {
  const max = Math.max(1, ...data.eventList.map((e) => e.athletes))
  return (
    <section className="compare-section">
      <h2>Frekwencja na zawodach</h2>
      <p className="muted small">Liczba zawodników w każdym komunikacie, w kolejności dat.</p>
      <div className="card table-card">
        <ul className="bars" aria-label="Liczba zawodników na zawodach">
          {data.eventList.map((e) => (
            <li key={e.pdfUrl} title={`${e.date ?? ''} ${e.name}: ${e.athletes} zawodników, ${e.starts} startów`}>
              <span className="bar-label">
                <span className="bar-date">{e.date ?? '—'}</span> {e.name}
              </span>
              <span className="bar-track">
                <span className="bar" style={{ width: `${(e.athletes / max) * 100}%` }} />
                <span className="bar-value">{e.athletes}</span>
              </span>
            </li>
          ))}
        </ul>
      </div>
    </section>
  )
}

function Clubs({ data }: { data: Ranking }) {
  const [all, setAll] = useState(false)
  const rows = all ? data.clubs : data.clubs.slice(0, 10)
  return (
    <section className="compare-section">
      <h2>Kluby</h2>
      <div className="card table-card table-scroll">
        <table className="ranking-table">
          <thead>
            <tr>
              <th>Klub</th>
              <th className="num">Zawodnicy</th>
              <th className="num">Starty</th>
              <th className="num">Podia</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((c) => (
              <tr key={c.club}>
                <td>{c.club}</td>
                <td className="num strong">{c.athletes}</td>
                <td className="num">{c.starts}</td>
                <td className="num">{c.podiums}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {data.clubs.length > 10 && (
        <div className="table-footer">
          <span className="muted small">
            Pokazano {rows.length} z {data.clubs.length}.
          </span>
          <button className="link-button" onClick={() => setAll((v) => !v)}>
            {all ? 'Pokaż mniej' : 'Pokaż wszystkie'}
          </button>
        </div>
      )}
    </section>
  )
}

/** "pistolet + karabin" → "P + K" (pełna nazwa w podpowiedzi). */
const shortScope = (scope: string | null) =>
  (scope ?? '')
    .split(' + ')
    .map((d) => d[0]?.toUpperCase())
    .join(' + ')

const normalize = (s: string) =>
  s
    .normalize('NFD')
    .replace(/\p{M}/gu, '')
    .replace(/ł/gi, 'l')
    .toLowerCase()
    .trim()
