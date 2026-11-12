import { useEffect, useState } from 'react'
import {
  DISCIPLINES,
  downloadLicensePdf,
  getLicensePlan,
  type Discipline,
  type LicensePlan,
  type LicenseRequest,
  type Missing,
} from '../api'

interface Props {
  name: string
  years: number[]
  defaultYear: number
}

const LABEL: Record<Discipline, string> = { pistolet: 'Pistolet', karabin: 'Karabin', strzelba: 'Strzelba' }

const missingText = (missing: Missing[]) => missing.map((m) => `${m.discipline} ${m.count}`).join(', ')

export function LicensePanel({ name, years, defaultYear }: Props) {
  const [year, setYear] = useState(defaultYear)
  const [licensed, setLicensed] = useState<Discipline[]>([])
  const [leading, setLeading] = useState<Discipline | null>(null)
  const [oneXPerRow, setOneXPerRow] = useState(false)

  // Wynik zapamiętany razem z ustawieniami, dla których go policzono.
  const [result, setResult] = useState<{ key: string; plan?: LicensePlan; error?: string } | null>(null)
  const [downloadError, setDownloadError] = useState<string | null>(null)
  const [downloading, setDownloading] = useState(false)

  const req: LicenseRequest = { name, year, licensed, leading, oneXPerRow }
  const reqKey = JSON.stringify(req)

  // Plan wniosku przelicza się po każdej zmianie ustawień.
  useEffect(() => {
    const controller = new AbortController()
    getLicensePlan(JSON.parse(reqKey), controller.signal)
      .then((plan) => setResult((cur) => ({ key: reqKey, plan: plan ?? cur?.plan })))
      .catch((e: Error) => {
        if (e.name !== 'AbortError') setResult((cur) => ({ key: reqKey, plan: cur?.plan, error: e.message }))
      })
    return () => controller.abort()
  }, [reqKey])

  const loading = result?.key !== reqKey
  const plan = result?.plan ?? null
  const error = (!loading && result?.error) || downloadError

  const toggleLicensed = (d: Discipline) =>
    setLicensed((cur) => (cur.includes(d) ? cur.filter((x) => x !== d) : DISCIPLINES.filter((x) => x === d || cur.includes(x))))

  const download = async () => {
    setDownloading(true)
    setDownloadError(null)
    try {
      await downloadLicensePdf(req)
    } catch (e) {
      setDownloadError((e as Error).message)
    } finally {
      setDownloading(false)
    }
  }

  return (
    <div className="license card">
      <div className="license-controls">
        <label className="field compact">
          <span className="field-label">Rok startów</span>
          <select value={year} onChange={(e) => setYear(Number(e.target.value))}>
            {years.map((y) => (
              <option key={y} value={y}>
                {y}
              </option>
            ))}
          </select>
        </label>

        <fieldset className="field compact">
          <legend className="field-label">Licencja</legend>
          <div className="chips">
            {DISCIPLINES.map((d) => (
              <button
                key={d}
                type="button"
                className={`chip ${licensed.includes(d) ? 'chip-on' : ''}`}
                aria-pressed={licensed.includes(d)}
                onClick={() => toggleLicensed(d)}
              >
                {LABEL[d]}
              </button>
            ))}
          </div>
          <span className="hint">{licensed.length === 0 ? 'Nic nie zaznaczono — zakres wykrywany automatycznie.' : ' '}</span>
        </fieldset>

        <label className="field compact">
          <span className="field-label">Dyscyplina wiodąca</span>
          <select value={leading ?? ''} onChange={(e) => setLeading((e.target.value || null) as Discipline | null)}>
            <option value="">automatycznie</option>
            {DISCIPLINES.map((d) => (
              <option key={d} value={d}>
                {LABEL[d]}
              </option>
            ))}
          </select>
        </label>

        <label className="toggle">
          <input type="checkbox" checked={oneXPerRow} onChange={(e) => setOneXPerRow(e.target.checked)} />
          <span>Jeden X w wierszu</span>
        </label>
      </div>

      {error && <p className="error">{error}</p>}

      {plan && (
        <div className={loading ? 'license-body stale' : 'license-body'}>
          <div className={`verdict ${plan.meetsRequirements ? 'ok' : 'bad'}`} role="status">
            <strong>
              {plan.meetsRequirements ? '✓ Spełnia warunki PZSS' : '✗ Nie spełnia warunków PZSS'}
            </strong>
            <span>
              {' '}
              dla licencji: {plan.licensed.join(' + ')}
              {plan.licensed.length > 1 ? ` (wiodąca: ${plan.leading})` : ''}
            </span>
            {!plan.meetsRequirements && <div>Brakuje startów: {missingText(plan.missing)}.</div>}
            {plan.meetsRequirements && plan.missingForFullLicense && (
              <div className="muted">Dla licencji na wszystkie dyscypliny brakuje: {missingText(plan.missingForFullLicense)}.</div>
            )}
          </div>

          <ul className="counts">
            {plan.counts.map((c) => (
              <li key={c.discipline} className={c.required === null ? 'off' : c.count >= c.required ? 'met' : 'unmet'}>
                <span>{LABEL[c.discipline]}</span>
                <strong>
                  {c.count}
                  {c.required !== null && `/${c.required}`}
                </strong>
                <small>{c.required === null ? 'poza licencją' : c.leading ? 'wiodąca' : 'poboczna'}</small>
              </li>
            ))}
          </ul>

          {plan.rows.length === 0 ? (
            <p className="muted">Brak startów w {plan.year} r. — wniosek będzie miał tylko nazwisko i rok.</p>
          ) : (
            <div className="table-scroll">
              <table className="form-table">
                <thead>
                  <tr>
                    <th className="num">Lp.</th>
                    <th>Nazwa zawodów</th>
                    <th>Data</th>
                    <th className="hide-narrow">Miejsce</th>
                    <th className="mark">P</th>
                    <th className="mark">K</th>
                    <th className="mark">S</th>
                  </tr>
                </thead>
                <tbody>
                  {plan.rows.map((r, i) => (
                    <tr key={i} className={i === 8 ? 'page-break' : ''}>
                      <td className="num">{i + 1}.</td>
                      <td>
                        {r.name}
                        {r.note && <span className="muted small"> · {r.note}</span>}
                      </td>
                      <td className="nowrap">{r.date}</td>
                      <td className="hide-narrow">{r.place ?? ''}</td>
                      <td className="mark">{r.pistol ? '✕' : ''}</td>
                      <td className="mark">{r.rifle ? '✕' : ''}</td>
                      <td className="mark">{r.shotgun ? '✕' : ''}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
          {plan.rows.length > 8 && (
            <p className="hint">Wzór ma 8 wierszy — kolejne trafią na drugą stronę wniosku.</p>
          )}

          <div className="form-actions">
            <button className="primary" onClick={download} disabled={downloading || loading}>
              {downloading ? 'Przygotowuję…' : 'Pobierz wniosek PDF'}
            </button>
            <span className="hint">
              Program wpisuje nazwisko, rok i starty. PESEL, numery licencji i podpisy uzupełnij ręcznie.
            </span>
          </div>
        </div>
      )}
      {!plan && loading && <p className="muted">Liczę starty…</p>}
    </div>
  )
}
