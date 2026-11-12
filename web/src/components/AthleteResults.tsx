import { useMemo, useState } from 'react'
import type { Start } from '../api'
import { LicensePanel } from './LicensePanel'

interface Props {
  query: string
  starts: Start[]
  years: number[]
  defaultYear: number
}

interface EventGroup {
  key: string
  date: string | null
  eventName: string
  documentTitle: string | null
  pdfUrl: string
  starts: Start[]
}

const normalize = (s: string) =>
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

export function AthleteResults({ query, starts, years, defaultYear }: Props) {
  const [licenseOpen, setLicenseOpen] = useState(false)

  // Jeden komunikat PDF = jedna karta zawodów.
  const events = useMemo(() => {
    const groups = new Map<string, EventGroup>()
    for (const s of starts) {
      const g = groups.get(s.pdfUrl) ?? {
        key: s.pdfUrl,
        date: s.date,
        eventName: s.eventName,
        documentTitle: s.documentTitle,
        pdfUrl: s.pdfUrl,
        starts: [],
      }
      g.starts.push(s)
      groups.set(s.pdfUrl, g)
    }
    return [...groups.values()]
  }, [starts])

  // Samo nazwisko może pasować do kilku osób.
  const athletes = useMemo(() => {
    const byKey = new Map<string, string>()
    for (const s of starts) if (!byKey.has(normalize(s.athlete))) byKey.set(normalize(s.athlete), s.athlete)
    return [...byKey.values()]
  }, [starts])

  return (
    <section className="athlete">
      <header className="athlete-header">
        <div>
          <h2>{query}</h2>
          <p className="muted">
            {starts.length === 0
              ? 'Nie znaleziono startów'
              : `${starts.length} ${plural(starts.length, 'start', 'starty', 'startów')} na ${events.length} ${plural(events.length, 'zawodach', 'zawodach', 'zawodach')}`}
          </p>
        </div>
        <button className={licenseOpen ? 'secondary active' : 'secondary'} onClick={() => setLicenseOpen((o) => !o)}>
          {licenseOpen ? 'Ukryj wniosek' : 'Wniosek PZSS'}
        </button>
      </header>

      {athletes.length > 1 && (
        <p className="notice">Pasuje kilka osób: {athletes.join(', ')}. Podaj imię i nazwisko, żeby zawęzić wyniki.</p>
      )}

      {licenseOpen && <LicensePanel name={query} years={years} defaultYear={defaultYear} />}

      <div className="events">
        {events.map((ev) => (
          <article key={ev.key} className="event card">
            <header className="event-header">
              <div>
                <span className="event-date">{ev.date ?? '—'}</span>
                <h3>{ev.eventName}</h3>
                {ev.documentTitle && ev.documentTitle !== ev.eventName && (
                  <span className="muted small">{ev.documentTitle}</span>
                )}
              </div>
              <a className="pdf-link" href={ev.pdfUrl} target="_blank" rel="noreferrer">
                PDF ↗
              </a>
            </header>
            <table className="starts">
              <colgroup>
                <col className="col-place" />
                <col />
                <col className="col-result" />
                <col className="col-club hide-narrow" />
              </colgroup>
              <thead>
                <tr>
                  <th className="num">Miejsce</th>
                  <th>Konkurencja</th>
                  <th className="num">Wynik</th>
                  <th className="hide-narrow">Klub</th>
                </tr>
              </thead>
              <tbody>
                {ev.starts.map((s, i) => (
                  <tr key={i}>
                    <td className="num">{s.place ? `${s.place}.` : '—'}</td>
                    <td>
                      {s.competition}
                      {athletes.length > 1 && <span className="muted small"> · {s.athlete}</span>}
                      {s.details.length > 0 && (
                        <div className="details">
                          {s.details.map((d) => (d.label ? `${d.label}: ${d.value}` : d.value)).join(' · ')}
                        </div>
                      )}
                    </td>
                    <td className="num result">{s.result ?? '?'}</td>
                    <td className="hide-narrow muted">{s.club ?? ''}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </article>
        ))}
      </div>
    </section>
  )
}

function plural(n: number, one: string, few: string, many: string) {
  if (n === 1) return one
  const last = n % 10
  const lastTwo = n % 100
  return last >= 2 && last <= 4 && (lastTwo < 12 || lastTwo > 14) ? few : many
}
