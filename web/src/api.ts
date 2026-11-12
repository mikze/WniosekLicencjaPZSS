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
  leading: Discipline
  licensed: Discipline[]
  counts: DisciplineCount[]
  meetsRequirements: boolean
  missing: Missing[]
  missingForFullLicense: Missing[] | null
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

export async function getLicensePlan(req: LicenseRequest, signal?: AbortSignal): Promise<LicensePlan> {
  return (await post('/api/license', req, signal)).json()
}

/** Pobiera uzupełniony wniosek PDF i zapisuje go w przeglądarce. */
export async function downloadLicensePdf(req: LicenseRequest): Promise<void> {
  const res = await post('/api/license/pdf', req)
  const blob = await res.blob()
  const fileName =
    /filename="?([^";]+)"?/.exec(res.headers.get('Content-Disposition') ?? '')?.[1] ?? `wniosek-${req.year}.pdf`
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = fileName
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}
