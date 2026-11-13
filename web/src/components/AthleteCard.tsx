import { useEffect, useState, type FormEvent } from 'react'
import { getAthlete, type AthleteProfile, type AthleteProfileResult } from '../api'
import { FormChart } from './FormChart'

interface Props {
  years: number[]
  active: boolean
  /** Zawodnik z adresu (#karta/Jan Nowak) albo z przycisku w innej zakładce. */
  requestedName: string | null
  onNameChange: (name: string) => void
}

const HISTORY_PAGE = 30
const fmt = (v: number) => v.toLocaleString('pl-PL', { maximumFractionDigits: 1 })

export function AthleteCard({ years, active, requestedName, onNameChange }: Props) {
  const [input, setInput] = useState(requestedName ?? '')
  const [selectedYears, setSelectedYears] = useState<number[] | null>(null) // null = wszystkie lata
  const yearsToSend = selectedYears ?? []
  const key = requestedName ? JSON.stringify({ name: requestedName, years: yearsToSend }) : null
  const [result, setResult] = useState<{ key: string; data?: AthleteProfileResult; error?: string } | null>(null)

  useEffect(() => {
    if (!active || !key) return
    const { name, years: y } = JSON.parse(key) as { name: string; years: number[] }
    const controller = new AbortController()
    getAthlete(name, y, controller.signal)
      .then((data) => setResult({ key, data }))
      .catch((e: Error) => {
        if (e.name !== 'AbortError') setResult({ key, error: e.message })
      })
    return () => controller.abort()
  }, [active, key])

  const loading = key !== null && result?.key !== key
  const data = result?.key === key ? result.data : undefined
  const allYears = selectedYears === null

  const submit = (e: FormEvent) => {
    e.preventDefault()
    if (input.trim()) onNameChange(input.trim())
  }
  const toggleYear = (y: number) => {
    const cur = selectedYears ?? years
    const next = cur.includes(y) ? cur.filter((x) => x !== y) : [...cur, y]
    setSelectedYears(next.length === 0 || next.length === years.length ? null : next)
  }

  return (
    <div className="athlete-card">
      <section className="card intro">
        <p>
          <strong>Karta zawodnika</strong> — wszystko o jednym strzelcu z wybranych lat: forma w konkurencjach na
          wykresie, rekordy osobiste, sezony z licencją PZSS, rywale oraz każdy start z miejscem na tle stawki
          („26. na 92 startujących”).
        </p>
        <form className="card-form" onSubmit={submit}>
          <label className="field compact card-name">
            <span className="field-label">Zawodnik</span>
            <input
              type="search"
              value={input}
              onChange={(e) => setInput(e.target.value)}
              placeholder="np. Jan Nowak albo samo nazwisko"
              spellCheck={false}
            />
          </label>
          <button className="primary" type="submit" disabled={!input.trim() || loading}>
            {loading ? 'Wczytuję…' : 'Pokaż kartę'}
          </button>
        </form>
        <fieldset className="field compact">
          <legend className="field-label">Lata</legend>
          <div className="chips">
            {years.map((y) => {
              const on = allYears || selectedYears!.includes(y)
              return (
                <button key={y} type="button" className={`chip ${on ? 'chip-on' : ''}`} aria-pressed={on} onClick={() => toggleYear(y)}>
                  {y}
                </button>
              )
            })}
            {!allYears && (
              <button type="button" className="link-button" onClick={() => setSelectedYears(null)}>
                wszystkie
              </button>
            )}
          </div>
        </fieldset>
      </section>

      {loading && (
        <p className="muted">Wczytuję kartę… Pierwsze wczytanie wszystkich lat może potrwać kilkanaście sekund.</p>
      )}
      {result?.error && result.key === key && <p className="error">{result.error}</p>}

      {data && !data.profile && data.candidates.length === 0 && (
        <p className="notice">Nie znaleziono zawodnika „{requestedName}” w wybranych latach.</p>
      )}
      {data && data.candidates.length > 0 && (
        <section className="card candidates">
          <p>„{requestedName}” pasuje do kilku osób — wybierz zawodnika:</p>
          <div className="chips">
            {data.candidates.map((c) => (
              <button
                key={c.name}
                type="button"
                className="chip"
                onClick={() => {
                  setInput(c.name)
                  onNameChange(c.name)
                }}
              >
                {c.name}
                <span className="muted small">
                  {' '}
                  · {c.club ?? 'bez klubu'} · {c.starts} st.
                </span>
              </button>
            ))}
          </div>
        </section>
      )}
      {data?.profile && <Profile profile={data.profile} />}
    </div>
  )
}

function Profile({ profile: p }: { profile: AthleteProfile }) {
  const chartable = p.records.filter((r) => r.count >= 2)
  const [competition, setCompetition] = useState(chartable[0]?.competition ?? '')
  const [historyLimit, setHistoryLimit] = useState(HISTORY_PAGE)
  const [allRecords, setAllRecords] = useState(false)
  const chosen = chartable.some((r) => r.competition === competition) ? competition : (chartable[0]?.competition ?? '')
  const chartStarts = p.history.filter((h) => h.group === chosen)
  const records = allRecords ? p.records : p.records.slice(0, 10)
  const history = [...p.history].reverse()

  return (
    <main className="results">
      <header className="athlete-header">
        <div>
          <h2>{p.name}</h2>
          <p className="muted">
            {p.club ?? 'klub nieznany'} · sezony {p.seasons.map((s) => s.year).sort().join(', ')}
          </p>
        </div>
      </header>

      <section className="tiles" aria-label="Podsumowanie">
        <div className="tile card">
          <span className="tile-label">Starty</span>
          <strong className="tile-value">{p.starts}</strong>
          <span className="tile-hint">na {p.events} zawodach</span>
        </div>
        <div className="tile card">
          <span className="tile-label">Podia (1. / 2. / 3.)</span>
          <strong className="tile-value">
            {p.gold} / {p.silver} / {p.bronze}
          </strong>
        </div>
        <div className="tile card">
          <span className="tile-label">Najlepsze miejsce</span>
          <strong className="tile-value">{p.bestPlace ? `${p.bestPlace}.` : '—'}</strong>
        </div>
        <div className="tile card">
          <span className="tile-label">Średnio lepszy niż</span>
          <strong className="tile-value">{p.averagePercentile !== null ? `${p.averagePercentile}%` : '—'}</strong>
          <span className="tile-hint">startujących w tej samej konkurencji</span>
        </div>
      </section>

      {chartable.length > 0 && (
        <section className="compare-section">
          <div className="section-head">
            <h2>Forma</h2>
            <select value={chosen} onChange={(e) => setCompetition(e.target.value)} aria-label="Konkurencja">
              {chartable.map((r) => (
                <option key={r.competition} value={r.competition}>
                  {r.competition} ({r.count})
                </option>
              ))}
            </select>
          </div>
          <div className="card table-card">
            <FormChart key={chosen} starts={chartStarts} />
          </div>
        </section>
      )}

      <section className="compare-section">
        <h2>Rekordy osobiste</h2>
        <div className="card table-card table-scroll">
          <table className="ranking-table">
            <thead>
              <tr>
                <th>Konkurencja</th>
                <th className="num">Rekord</th>
                <th className="hide-narrow">Kiedy</th>
                <th className="num">Startów</th>
                <th className="num">Średnio</th>
                <th className="num hide-narrow">Ostatnio</th>
              </tr>
            </thead>
            <tbody>
              {records.map((r) => (
                <tr key={r.competition}>
                  <td>{r.competition}</td>
                  <td className="num strong">{r.best}</td>
                  <td className="hide-narrow muted small">
                    {r.bestDate} · {r.bestEvent}
                  </td>
                  <td className="num">{r.count}</td>
                  <td className="num">{fmt(r.average)}</td>
                  <td className="num hide-narrow muted">{r.last}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        {p.records.length > 10 && (
          <div className="table-footer">
            <span className="muted small">
              Pokazano {records.length} z {p.records.length} konkurencji (najczęściej strzelane).
            </span>
            <button className="link-button" onClick={() => setAllRecords((v) => !v)}>
              {allRecords ? 'Pokaż mniej' : 'Pokaż wszystkie'}
            </button>
          </div>
        )}
      </section>

      <section className="compare-section">
        <h2>Sezony</h2>
        <div className="card table-card table-scroll">
          <table className="ranking-table">
            <thead>
              <tr>
                <th>Rok</th>
                <th className="num">Starty</th>
                <th className="num">Zawody</th>
                <th className="num">1. / 2. / 3.</th>
                <th>Licencja PZSS</th>
              </tr>
            </thead>
            <tbody>
              {p.seasons.map((s) => (
                <tr key={s.year}>
                  <td className="strong">{s.year}</td>
                  <td className="num">{s.starts}</td>
                  <td className="num">{s.events}</td>
                  <td className="num">
                    {s.gold} / {s.silver} / {s.bronze}
                  </td>
                  <td className={s.meetsLicense ? 'license-ok' : 'muted'}>
                    {s.meetsLicense ? `✓ ${s.licenseScope}` : '— nie spełnia'}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      {p.rivals.length > 0 && (
        <section className="compare-section">
          <h2>Rywale</h2>
          <p className="muted small">
            Zawodnicy, z którymi najczęściej startował w tej samej konkurencji, i bilans: ile razy był przed nimi, a ile za
            nimi.
          </p>
          <div className="card table-card table-scroll">
            <table className="ranking-table">
              <thead>
                <tr>
                  <th>Zawodnik</th>
                  <th className="hide-narrow">Klub</th>
                  <th className="num">Wspólne starty</th>
                  <th className="num">Bilans</th>
                </tr>
              </thead>
              <tbody>
                {p.rivals.map((r) => (
                  <tr key={r.name}>
                    <td>{r.name}</td>
                    <td className="hide-narrow muted">{r.club ?? ''}</td>
                    <td className="num">{r.common}</td>
                    <td className="num strong">
                      <span className="win">{r.wins}</span> – <span className="loss">{r.losses}</span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      )}

      <section className="compare-section">
        <h2>Wszystkie starty</h2>
        <div className="card table-card table-scroll">
          <table className="ranking-table">
            <thead>
              <tr>
                <th>Data</th>
                <th>Zawody</th>
                <th>Konkurencja</th>
                <th className="num">Wynik</th>
                <th className="num">Miejsce</th>
                <th className="num hide-narrow">Lepszy niż</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {history.slice(0, historyLimit).map((h, i) => (
                <tr key={i}>
                  <td className="muted">{h.date}</td>
                  <td className="wrap">{h.eventName}</td>
                  <td className="wrap">{h.competition}</td>
                  <td className="num strong">{h.result ?? '—'}</td>
                  <td className="num">{h.place ? `${h.place}. / ${h.field}` : `— / ${h.field}`}</td>
                  <td className="num hide-narrow muted">{h.percentile !== null ? `${Math.round(h.percentile)}%` : '—'}</td>
                  <td>
                    <a className="pdf-link" href={h.pdfUrl} target="_blank" rel="noreferrer">
                      PDF ↗
                    </a>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        {history.length > historyLimit && (
          <div className="table-footer">
            <span className="muted small">
              Pokazano {historyLimit} z {history.length}.
            </span>
            <button className="link-button" onClick={() => setHistoryLimit((l) => l + HISTORY_PAGE * 3)}>
              Pokaż więcej
            </button>
          </div>
        )}
      </section>
    </main>
  )
}
