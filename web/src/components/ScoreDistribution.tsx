import { useState } from 'react'
import type { CompetitionStat } from '../api'

interface Props {
  competitions: CompetitionStat[]
}

interface Bin {
  from: number
  to: number
  label: string
  count: number
}

const fmt = (v: number) => v.toLocaleString('pl-PL', { maximumFractionDigits: 1 })

/** Przedziały histogramu: dla małych skal całkowitych (np. strzelba 0–5) jeden słupek na wynik, inaczej najwyżej ~25 „ładnych” przedziałów. */
function makeBins(values: number[]): Bin[] {
  if (values.length === 0) return []
  const min = values[0]
  const max = values[values.length - 1]
  const allInt = values.every(Number.isInteger)

  let width: number
  if (allInt && max - min <= 25) width = 1
  else {
    // Najmniejsza „ładna” szerokość, przy której wychodzi najwyżej ~25 przedziałów.
    const raw = Math.max((max - min) / 25, 1e-9)
    const pow = 10 ** Math.floor(Math.log10(raw))
    width = [1, 2, 2.5, 5, 10].map((m) => m * pow).find((w) => w >= raw) ?? 10 * pow
    if (allInt) width = Math.max(1, Math.round(width))
  }

  const start = Math.floor(min / width) * width
  const bins: Bin[] = []
  for (let from = start; from <= max; from += width) {
    const to = from + width
    const label =
      width === 1 && allInt ? fmt(from) : allInt ? `${fmt(from)}–${fmt(to - 1)}` : `${fmt(from)}–${fmt(to)}`
    bins.push({ from, to, label, count: 0 })
  }
  for (const v of values) {
    const i = Math.min(bins.length - 1, Math.floor((v - start) / width))
    bins[i].count++
  }
  return bins
}

function quantile(sorted: number[], q: number) {
  const pos = (sorted.length - 1) * q
  const lo = Math.floor(pos)
  const hi = Math.ceil(pos)
  return sorted[lo] + (sorted[hi] - sorted[lo]) * (pos - lo)
}

export function ScoreDistribution({ competitions }: Props) {
  const withScores = competitions.filter((c) => c.scores.length >= 5)
  const [index, setIndex] = useState(0)
  const [hover, setHover] = useState<number | null>(null)
  const [asTable, setAsTable] = useState(false)
  const comp = withScores[Math.min(index, withScores.length - 1)]

  if (!comp) return null
  // Serwer wysyła wyniki już posortowane rosnąco.
  const scores = comp.scores
  const bins = makeBins(scores)
  const stats = {
    n: scores.length,
    mean: scores.reduce((a, b) => a + b, 0) / scores.length,
    median: quantile(scores, 0.5),
    q1: quantile(scores, 0.25),
    q3: quantile(scores, 0.75),
    max: scores[scores.length - 1],
  }
  const maxCount = Math.max(...bins.map((b) => b.count))
  const active = hover !== null ? bins[hover] : null

  return (
    <section className="compare-section">
      <div className="section-head">
        <h2>Rozkład punktów</h2>
        <select
          value={index}
          onChange={(e) => {
            setIndex(Number(e.target.value))
            setHover(null)
          }}
          aria-label="Konkurencja"
        >
          {withScores.map((c, i) => (
            <option key={c.name} value={i}>
              {c.name} ({c.scores.length})
            </option>
          ))}
        </select>
      </div>
      <p className="muted small">
        Wyniki wszystkich startów w roku — ile razy padł wynik z danego przedziału. Pokazuje, jak trudna jest konkurencja
        i gdzie wypada typowy wynik.
      </p>

      <div className="tiles dist-stats">
        <Stat label="Startów" value={stats.n.toLocaleString('pl-PL')} />
        <Stat label="Średnia" value={fmt(stats.mean)} />
        <Stat label="Mediana" value={fmt(stats.median)} hint="połowa ma co najmniej tyle" />
        <Stat label="Środkowe 50%" value={`${fmt(stats.q1)}–${fmt(stats.q3)}`} />
        <Stat label="Najlepszy" value={fmt(stats.max)} />
      </div>

      <div className="card table-card">
        <div className="dist-head">
          <span className="dist-readout" aria-live="polite">
            {active ? (
              <>
                <strong>{active.label} pkt</strong>: {active.count} {plural(active.count)} (
                {fmt((active.count / stats.n) * 100)}%)
              </>
            ) : (
              <span className="muted">Najedź na słupek, żeby zobaczyć szczegóły.</span>
            )}
          </span>
          <button className="link-button" onClick={() => setAsTable((v) => !v)}>
            {asTable ? 'Pokaż wykres' : 'Pokaż jako tabelę'}
          </button>
        </div>

        {asTable ? (
          <div className="table-scroll">
            <table className="ranking-table">
              <thead>
                <tr>
                  <th>Punkty</th>
                  <th className="num">Startów</th>
                  <th className="num">Udział</th>
                </tr>
              </thead>
              <tbody>
                {bins.map((b) => (
                  <tr key={b.from}>
                    <td>{b.label}</td>
                    <td className="num strong">{b.count}</td>
                    <td className="num muted">{fmt((b.count / stats.n) * 100)}%</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <div className="histogram" role="img" aria-label={`Rozkład punktów: ${comp.name}`} onMouseLeave={() => setHover(null)}>
            <div className="hist-bars">
              {bins.map((b, i) => (
                <button
                  key={b.from}
                  className={hover === i ? 'hist-col active' : 'hist-col'}
                  onMouseEnter={() => setHover(i)}
                  onFocus={() => setHover(i)}
                  aria-label={`${b.label} pkt: ${b.count} ${plural(b.count)}`}
                >
                  <span className="hist-bar" style={{ height: `${(b.count / maxCount) * 100}%` }} />
                </button>
              ))}
            </div>
            <div className="hist-axis">
              <span>{bins[0].label}</span>
              {bins.length > 2 && <span>{bins[Math.floor(bins.length / 2)].label}</span>}
              <span>{bins[bins.length - 1].label}</span>
            </div>
            <div className="hist-axis-title muted small">punkty →</div>
          </div>
        )}
      </div>
    </section>
  )
}

function Stat({ label, value, hint }: { label: string; value: string; hint?: string }) {
  return (
    <div className="tile card">
      <span className="tile-label">{label}</span>
      <strong className="tile-value small-value">{value}</strong>
      {hint && <span className="tile-hint">{hint}</span>}
    </div>
  )
}

function plural(n: number) {
  if (n === 1) return 'start'
  const l = n % 10
  const t = n % 100
  return l >= 2 && l <= 4 && (t < 12 || t > 14) ? 'starty' : 'startów'
}
