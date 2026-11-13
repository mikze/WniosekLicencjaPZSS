import { useMemo, useRef, useState } from 'react'
import { search, type Start } from '../api'
import { buildComparison, parsePlace, type Comparison } from '../compare'
import { SearchForm } from './SearchForm'

interface Props {
  years: number[]
}

// Kolor zawodnika — ten sam we wszystkich tabelach, żeby łatwo było go znaleźć.
const COLORS = ['var(--c1)', 'var(--c2)', 'var(--c3)', 'var(--c4)', 'var(--c5)', 'var(--c6)']

export function CompareView({ years }: Props) {
  const [data, setData] = useState<{ names: string[]; years: number[]; results: Start[]; pdfCount: number } | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [onlyShared, setOnlyShared] = useState(true)
  const abortRef = useRef<AbortController | null>(null)

  const run = async (names: string[], selectedYears: number[]) => {
    abortRef.current?.abort()
    const controller = new AbortController()
    abortRef.current = controller
    setLoading(true)
    setError(null)
    try {
      const res = await search(names, selectedYears, controller.signal)
      setData({ names, years: selectedYears, results: res.results, pdfCount: res.pdfCount })
    } catch (e) {
      if ((e as Error).name !== 'AbortError') setError((e as Error).message)
    } finally {
      if (abortRef.current === controller) setLoading(false)
    }
  }

  const cmp = useMemo(() => (data ? buildComparison(data.results, data.names) : null), [data])

  return (
    <div className="compare">
      <section className="card intro">
        <p>
          <strong>Porównaj zawodników</strong> — zestaw obok siebie od 2 do 6 strzelców i sprawdź, kto był lepszy tam, gdzie
          startowali razem. Program bierze tylko konkurencje rozegrane na tych samych zawodach, więc porównanie jest
          uczciwe.
        </p>
        <ol className="steps">
          <li>
            <strong>Wpisz zawodników</strong>
            <span>po jednym w linii, od 2 do 6 osób</span>
          </li>
          <li>
            <strong>Wybierz lata i kliknij „Porównaj”</strong>
            <span>im więcej lat, tym więcej wspólnych startów</span>
          </li>
          <li>
            <strong>Przeczytaj wyniki</strong>
            <span>podsumowanie, pojedynki i starty obok siebie</span>
          </li>
        </ol>
        <details className="how">
          <summary>Jak to działa?</summary>
          <ul>
            <li>
              <strong>Pojedynek</strong> to jedna konkurencja na jednych zawodach, w której startowali obaj zawodnicy.
              Wygrywa ten, kto zajął wyższe miejsce, a gdy protokół nie podaje miejsc — kto ma więcej punktów.
            </li>
            <li>
              <strong>Tabela pojedynków</strong>: w wierszu zawodnik, w kolumnie rywal; „7 – 2” znaczy, że zawodnik z wiersza
              był lepszy 7 razy, a gorszy 2 razy. Zielone tło — prowadzi w bilansie.
            </li>
            <li>
              <strong>Starty obok siebie</strong>: każde zawody i konkurencja w osobnym wierszu, wynik każdego zawodnika
              w jego kolumnie (miejsce w nawiasie); najlepszy wynik jest wyróżniony.
            </li>
            <li>
              <strong>Najlepsze wyniki w konkurencjach</strong>: rekord każdego zawodnika w danej konkurencji z wybranych lat
              (porównywane są punkty, bo miejsca z różnych zawodów nie są porównywalne).
            </li>
            <li>Każdy zawodnik ma swój kolor we wszystkich tabelach, a obok koloru zawsze jest jego nazwisko.</li>
          </ul>
        </details>
      </section>

      <SearchForm
        years={years}
        loading={loading}
        onSearch={run}
        submitLabel="Porównaj"
        placeholder={'Jan Nowak\nKowalska Anna\nPiotr Wiśniewski'}
        minNames={2}
        maxNames={6}
      />
      {error && <p className="error">{error}</p>}
      {cmp && data && (
        <main className="results">
          <div className="results-bar">
            <span className="muted">
              Przejrzano {data.pdfCount} komunikatów z {data.years.length === 1 ? 'roku' : 'lat'} {[...data.years].sort().join(', ')}.
            </span>
          </div>
          <Summaries cmp={cmp} />
          <HeadToHead cmp={cmp} />
          <section className="compare-section">
            <div className="section-head">
              <h2>Starty obok siebie</h2>
              <label className="toggle">
                <input type="checkbox" checked={onlyShared} onChange={(e) => setOnlyShared(e.target.checked)} />
                <span>Tylko konkurencje, w których startowało co najmniej dwóch</span>
              </label>
            </div>
            <SideBySide cmp={cmp} onlyShared={onlyShared} />
          </section>
          <BestResults cmp={cmp} onlyShared={onlyShared} />
        </main>
      )}
    </div>
  )
}

function NameTag({ cmp, name }: { cmp: Comparison; name: string }) {
  return (
    <span className="name-tag">
      <span className="dot" style={{ background: COLORS[cmp.names.indexOf(name) % COLORS.length] }} aria-hidden />
      {name}
    </span>
  )
}

function Summaries({ cmp }: { cmp: Comparison }) {
  return (
    <section className="compare-section">
      <h2>Podsumowanie</h2>
      <div className="summary-grid">
        {cmp.summaries.map((s) => (
          <article key={s.name} className="card summary">
            <h3>
              <NameTag cmp={cmp} name={s.name} />
            </h3>
            {s.starts === 0 ? (
              <p className="muted">Nie znaleziono startów.</p>
            ) : (
              <dl>
                <dt>Starty</dt>
                <dd>{s.starts}</dd>
                <dt>Zawody</dt>
                <dd>{s.events}</dd>
                <dt>Wspólne starty</dt>
                <dd>{s.sharedStarts}</dd>
                <dt>Pojedynki</dt>
                <dd>
                  <span className="win">{s.wins}</span> – <span className="loss">{s.losses}</span>
                </dd>
                <dt>Średnie miejsce</dt>
                <dd>{s.avgPlace === null ? '—' : s.avgPlace.toFixed(1).replace('.', ',')}</dd>
                <dt>Najlepsze miejsce</dt>
                <dd>{s.bestPlace ?? '—'}</dd>
              </dl>
            )}
            {s.athletes.length > 1 && <p className="notice small">Pasuje kilka osób: {s.athletes.join(', ')}</p>}
          </article>
        ))}
      </div>
    </section>
  )
}

function HeadToHead({ cmp }: { cmp: Comparison }) {
  const anyDuel = cmp.names.some((a) => cmp.names.some((b) => cmp.headToHead[a][b] > 0))
  return (
    <section className="compare-section">
      <h2>Pojedynki</h2>
      <p className="muted small">
        Ile razy zawodnik z wiersza wyprzedził zawodnika z kolumny w tej samej konkurencji na tych samych zawodach
        (liczy się miejsce, a gdy go brak — wynik).
      </p>
      {!anyDuel ? (
        <p className="muted">Ci zawodnicy nie startowali razem w żadnej konkurencji.</p>
      ) : (
        <div className="table-scroll">
          <table className="matrix">
            <thead>
              <tr>
                <th />
                {cmp.names.map((n) => (
                  <th key={n}>
                    <NameTag cmp={cmp} name={n} />
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {cmp.names.map((a) => (
                <tr key={a}>
                  <th scope="row">
                    <NameTag cmp={cmp} name={a} />
                  </th>
                  {cmp.names.map((b) => {
                    if (a === b) return <td key={b} className="self" />
                    const w = cmp.headToHead[a][b]
                    const l = cmp.headToHead[b][a]
                    return (
                      <td key={b} className={w > l ? 'lead' : w < l ? 'trail' : ''}>
                        {w + l === 0 ? '—' : `${w} – ${l}`}
                      </td>
                    )
                  })}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  )
}

function Cell({ start, best }: { start: Start | undefined; best: boolean }) {
  if (!start) return <td className="cell empty">·</td>
  const place = parsePlace(start.place)
  return (
    <td className={best ? 'cell best' : 'cell'}>
      <strong>{start.result ?? '?'}</strong>
      {place !== null && <span className="muted small"> ({place}.)</span>}
    </td>
  )
}

function SideBySide({ cmp, onlyShared }: { cmp: Comparison; onlyShared: boolean }) {
  const events = cmp.events
    .map((ev) => ({ ...ev, rows: onlyShared ? ev.rows.filter((r) => r.entries.size > 1) : ev.rows }))
    .filter((ev) => ev.rows.length > 0)

  if (events.length === 0)
    return <p className="muted">Brak wspólnych startów. Odznacz filtr powyżej, żeby zobaczyć wszystkie starty.</p>

  return (
    <div className="events">
      {events.map((ev) => (
        <article key={ev.key} className="event card">
          <header className="event-header">
            <div>
              <span className="event-date">{ev.date ?? '—'}</span>
              <h3>{ev.eventName}</h3>
            </div>
            <a className="pdf-link" href={ev.pdfUrl} target="_blank" rel="noreferrer">
              PDF ↗
            </a>
          </header>
          <div className="table-scroll">
            <table className="side">
              <thead>
                <tr>
                  <th>Konkurencja</th>
                  {cmp.names.map((n) => (
                    <th key={n} className="cell">
                      <NameTag cmp={cmp} name={n} />
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {ev.rows.map((row) => (
                  <tr key={row.key}>
                    <td>{row.competition}</td>
                    {cmp.names.map((n) => (
                      <Cell key={n} start={row.entries.get(n)} best={row.best.has(n)} />
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </article>
      ))}
    </div>
  )
}

function BestResults({ cmp, onlyShared }: { cmp: Comparison; onlyShared: boolean }) {
  const rows = onlyShared ? cmp.bestByCompetition.filter((b) => b.perAthlete.size > 1) : cmp.bestByCompetition
  return (
    <section className="compare-section">
      <h2>Najlepsze wyniki w konkurencjach</h2>
      <p className="muted small">
        Najwyższy wynik każdego zawodnika w danej konkurencji w wybranych latach, w nawiasie liczba startów.
      </p>
      {rows.length === 0 ? (
        <p className="muted">Brak konkurencji do zestawienia.</p>
      ) : (
        <div className="card table-card table-scroll">
          <table className="side">
            <thead>
              <tr>
                <th>Konkurencja</th>
                {cmp.names.map((n) => (
                  <th key={n} className="cell">
                    <NameTag cmp={cmp} name={n} />
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {rows.map((b) => (
                <tr key={b.competition}>
                  <td>{b.competition}</td>
                  {cmp.names.map((n) => {
                    const v = b.perAthlete.get(n)
                    if (!v) return <td key={n} className="cell empty">·</td>
                    return (
                      <td key={n} className={b.leader.has(n) ? 'cell best' : 'cell'} title={`${v.best.date ?? ''} ${v.best.eventName}`}>
                        <strong>{v.best.result}</strong>
                        <span className="muted small"> ({v.count})</span>
                        <div className="muted small">{v.best.date?.replace(/\s*r\.?$/, '')}</div>
                      </td>
                    )
                  })}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  )
}
