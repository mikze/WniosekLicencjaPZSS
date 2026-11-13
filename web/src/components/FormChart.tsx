import { useState } from 'react'
import type { ProfileStart } from '../api'

interface Props {
  starts: ProfileStart[]
}

const W = 760
const H = 240
const PAD = { top: 16, right: 16, bottom: 28, left: 44 }

const fmt = (v: number) => v.toLocaleString('pl-PL', { maximumFractionDigits: 1 })
const toTime = (sortDate: string) => new Date(`${sortDate}T00:00:00`).getTime()

/**
 * Wykres formy: wynik w jednej konkurencji na kolejnych zawodach (jedna seria — bez legendy).
 * Oś X to czas, oś Y to punkty; najlepszy wynik jest wyróżniony, a po najechaniu pokazuje się szczegół.
 */
export function FormChart({ starts }: Props) {
  const points = starts.filter((s) => s.score !== null && s.sortDate !== '9999')
  const [hover, setHover] = useState<number | null>(null)
  if (points.length < 2) return <p className="muted">Za mało startów w tej konkurencji, żeby narysować wykres.</p>

  const times = points.map((p) => toTime(p.sortDate))
  const scores = points.map((p) => p.score!)
  const tMin = Math.min(...times)
  const tMax = Math.max(...times)
  const sMax = Math.max(...scores)
  const sMin = Math.min(...scores)
  // Oś Y od „ładnej” wartości poniżej minimum, żeby zmiany były widoczne, ale bez przesady.
  const span = Math.max(sMax - sMin, 1)
  const yMin = Math.max(0, Math.floor((sMin - span * 0.15) / 5) * 5)
  const yMax = Math.ceil((sMax + span * 0.1) / 5) * 5 || 1

  const x = (t: number) => PAD.left + (tMax === tMin ? 0.5 : (t - tMin) / (tMax - tMin)) * (W - PAD.left - PAD.right)
  const y = (s: number) => PAD.top + (1 - (s - yMin) / (yMax - yMin)) * (H - PAD.top - PAD.bottom)

  const best = scores.indexOf(sMax)
  const path = points.map((p, i) => `${i ? 'L' : 'M'}${x(times[i]).toFixed(1)},${y(p.score!).toFixed(1)}`).join(' ')
  const gridValues = [yMin, (yMin + yMax) / 2, yMax]
  const years = [...new Set(points.map((p) => p.year))]
  const active = hover !== null ? points[hover] : null

  // Najbliższy punkt w poziomie do kursora.
  const onMove = (e: React.MouseEvent<SVGSVGElement>) => {
    const rect = e.currentTarget.getBoundingClientRect()
    const px = ((e.clientX - rect.left) / rect.width) * W
    let nearest = 0
    for (let i = 1; i < points.length; i++) if (Math.abs(x(times[i]) - px) < Math.abs(x(times[nearest]) - px)) nearest = i
    setHover(nearest)
  }

  return (
    <div className="form-chart">
      <div className="dist-readout" aria-live="polite">
        {active ? (
          <>
            <strong>{fmt(active.score!)} pkt</strong> · {active.date} · {active.eventName}
            {active.place && (
              <span className="muted">
                {' '}
                · {active.place}. na {active.field}
              </span>
            )}
          </>
        ) : (
          <span className="muted">
            Rekord: <strong>{fmt(sMax)} pkt</strong> ({points[best].date}). Najedź na wykres, żeby zobaczyć szczegóły.
          </span>
        )}
      </div>
      <svg
        viewBox={`0 0 ${W} ${H}`}
        role="img"
        aria-label={`Wyniki w kolejnych startach, od ${fmt(sMin)} do ${fmt(sMax)} pkt`}
        onMouseMove={onMove}
        onMouseLeave={() => setHover(null)}
      >
        {gridValues.map((v) => (
          <g key={v}>
            <line className="grid" x1={PAD.left} x2={W - PAD.right} y1={y(v)} y2={y(v)} />
            <text className="axis-label" x={PAD.left - 8} y={y(v) + 4} textAnchor="end">
              {fmt(v)}
            </text>
          </g>
        ))}
        {years.map((yr) => {
          const first = points.find((p) => p.year === yr)!
          return (
            <text key={yr} className="axis-label" x={x(toTime(first.sortDate))} y={H - 8} textAnchor="middle">
              {yr}
            </text>
          )
        })}
        {active && (
          <line className="crosshair" x1={x(times[hover!])} x2={x(times[hover!])} y1={PAD.top} y2={H - PAD.bottom} />
        )}
        <path className="line" d={path} />
        {points.map((p, i) => (
          <circle
            key={i}
            className={i === best ? 'dot best' : i === hover ? 'dot active' : 'dot'}
            cx={x(times[i])}
            cy={y(p.score!)}
            r={i === best || i === hover ? 6 : 4}
          />
        ))}
      </svg>
    </div>
  )
}
