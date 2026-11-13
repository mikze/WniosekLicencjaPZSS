// Typy i wywołania API serwera (src/SzukajkaStartow.Web).

export type Discipline = 'pistolet' | 'karabin' | 'strzelba'
export const DISCIPLINES: Discipline[] = ['pistolet', 'karabin', 'strzelba']

export interface Start {
  query: string
  athlete: string
  date: string | null
  eventName: string
  documentTitle: string | null
  competition: string
  shootingDate: string | null
  place: string | null
  club: string | null
  result: string | null
  details: { label: string; value: string }[]
  pdfUrl: string
  page: number
}

export interface SearchResponse {
  results: Start[]
  pdfCount: number
  warnings: string[]
}

export interface LicenseRequest {
  name: string
  year: number
  /** Puste = program sam wykrywa zakres licencji. */
  licensed: Discipline[]
  /** null = program sam wybiera dyscyplinę wiodącą. */
  leading: Discipline | null
  oneXPerRow: boolean
  /** true = wszystkie starty z roku; false = tylko tyle, ile trzeba do spełnienia warunków. */
  allStarts: boolean
}

export interface LicenseRow {
  name: string
  date: string
  place: string | null
  pistol: boolean
  rifle: boolean
  shotgun: boolean
  note: string | null
}

export interface DisciplineCount {
  discipline: Discipline
  count: number
  /** null = dyscyplina poza licencją. */
  required: number | null
  leading: boolean
}

export interface Missing {
  discipline: Discipline
  count: number
}

export interface LicensePlan {
  athlete: string
  year: number
  rows: LicenseRow[]
  /** Ile wierszy byłoby przy wpisaniu wszystkich startów. */
  availableRows: number
  leading: Discipline
  licensed: Discipline[]
  counts: DisciplineCount[]
  meetsRequirements: boolean
  missing: Missing[]
  missingForFullLicense: Missing[] | null
}

export interface AthleteStat {
  name: string
  club: string | null
  starts: number
  events: number
  gold: number
  silver: number
  bronze: number
  pistol: number
  rifle: number
  shotgun: number
  meetsLicense: boolean
  licenseScope: string | null
}

export interface TopResult {
  athlete: string
  club: string | null
  result: string
  place: string | null
  date: string | null
  eventName: string
}

export interface CompetitionStat {
  name: string
  starters: number
  starts: number
  events: number
  top: TopResult[]
  /** Wyniki wszystkich startów (posortowane) — do rozkładu punktów. */
  scores: number[]
}

export interface ClubStat {
  club: string
  athletes: number
  starts: number
  podiums: number
}

export interface EventStat {
  date: string | null
  name: string
  pdfUrl: string
  athletes: number
  starts: number
}

export interface Ranking {
  year: number
  events: number
  starts: number
  athleteCount: number
  clubCount: number
  meetingLicense: number
  athletes: AthleteStat[]
  competitions: CompetitionStat[]
  clubs: ClubStat[]
  eventList: EventStat[]
  /** Wszystkie kluby z roku — do listy wyboru filtra. */
  availableClubs: string[]
  /** Kluby, według których przefiltrowano statystyki (puste = wszystkie). */
  selectedClubs: string[]
}

export interface AthleteCandidate {
  name: string
  club: string | null
  starts: number
}

export interface ProfileStart {
  year: number
  date: string | null
  sortDate: string
  eventName: string
  competition: string
  place: string | null
  result: string | null
  score: number | null
  /** Liczba zawodników w tej konkurencji na tych zawodach. */
  field: number
  /** Jaki procent stawki wyprzedził (0–100). */
  percentile: number | null
  pdfUrl: string
  /** Wspólna nazwa konkurencji z różnych lat (do wykresu formy i rekordów). */
  group: string
}

export interface PersonalRecord {
  competition: string
  best: string
  bestDate: string | null
  bestEvent: string
  count: number
  average: number
  last: string | null
  lastDate: string | null
}

export interface SeasonStat {
  year: number
  starts: number
  events: number
  gold: number
  silver: number
  bronze: number
  meetsLicense: boolean
  licenseScope: string | null
}

export interface Rival {
  name: string
  club: string | null
  common: number
  wins: number
  losses: number
}

export interface AthleteProfile {
  name: string
  club: string | null
  starts: number
  events: number
  gold: number
  silver: number
  bronze: number
  bestPlace: number | null
  averagePercentile: number | null
  seasons: SeasonStat[]
  records: PersonalRecord[]
  rivals: Rival[]
  history: ProfileStart[]
}

export interface AthleteProfileResult {
  profile: AthleteProfile | null
  candidates: AthleteCandidate[]
}

export async function getAthlete(name: string, years: number[], signal?: AbortSignal): Promise<AthleteProfileResult> {
  const params = new URLSearchParams({ name })
  for (const y of years) params.append('year', String(y))
  return (await request(`/api/athlete?${params}`, { signal })).json()
}

async function request(path: string, init?: RequestInit): Promise<Response> {
  const res = await fetch(path, init)
  if (!res.ok) {
    let message = `Błąd serwera (${res.status})`
    try {
      const body = await res.json()
      if (body?.error) message = body.error
    } catch {
      // odpowiedź bez JSON-a — zostaje ogólny komunikat
    }
    throw new Error(message)
  }
  return res
}

const post = (path: string, body: unknown, signal?: AbortSignal) =>
  request(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
    signal,
  })

export async function getYears(): Promise<number[]> {
  return (await request('/api/years')).json()
}

export async function search(names: string[], years: number[], signal?: AbortSignal): Promise<SearchResponse> {
  return (await post('/api/search', { names, years }, signal)).json()
}

export async function getRanking(year: number, clubs: string[], signal?: AbortSignal): Promise<Ranking> {
  const params = new URLSearchParams({ year: String(year) })
  for (const c of clubs) params.append('club', c)
  return (await request(`/api/ranking?${params}`, { signal })).json()
}

export async function getLicensePlan(req: LicenseRequest, signal?: AbortSignal): Promise<LicensePlan> {
  return (await post('/api/license', req, signal)).json()
}

/** Uzupełniony wniosek PDF (do podglądu albo zapisania). */
export async function fetchLicensePdf(req: LicenseRequest, signal?: AbortSignal): Promise<{ blob: Blob; fileName: string }> {
  const res = await post('/api/license/pdf', req, signal)
  const fileName =
    /filename="?([^";]+)"?/.exec(res.headers.get('Content-Disposition') ?? '')?.[1] ?? `wniosek-${req.year}.pdf`
  return { blob: await res.blob(), fileName }
}

/** Zapisuje plik w przeglądarce (pobieranie). */
export function saveBlob(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = fileName
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}
