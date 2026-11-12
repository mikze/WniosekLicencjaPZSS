import { useEffect, useRef, useState } from 'react'
import { getYears, search, type SearchResponse } from './api'
import { AthleteResults } from './components/AthleteResults'
import { SearchForm } from './components/SearchForm'
import { downloadCsv } from './csv'

const CURRENT_YEAR = new Date().getFullYear()

interface SearchState {
  names: string[]
  years: number[]
  response: SearchResponse
}

export default function App() {
  const [years, setYears] = useState<number[]>([])
  const [state, setState] = useState<SearchState | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const abortRef = useRef<AbortController | null>(null)

  useEffect(() => {
    getYears()
      .then(setYears)
      .catch(() => setYears([CURRENT_YEAR]))
  }, [])

  const runSearch = async (names: string[], selectedYears: number[]) => {
    abortRef.current?.abort()
    const controller = new AbortController()
    abortRef.current = controller
    setLoading(true)
    setError(null)
    try {
      const response = await search(names, selectedYears, controller.signal)
      setState({ names, years: selectedYears, response })
    } catch (e) {
      if ((e as Error).name !== 'AbortError') setError((e as Error).message)
    } finally {
      if (abortRef.current === controller) setLoading(false)
    }
  }

  const defaultYear = state ? Math.max(...state.years) : (years[0] ?? CURRENT_YEAR)

  return (
    <div className="page">
      <header className="masthead">
        <h1>Szukajka startów</h1>
        <p className="muted">
          Wyniki zawodników z komunikatów KS AGAT Złotoryja i wniosek PZSS o przedłużenie licencji.
        </p>
      </header>

      <SearchForm years={years} loading={loading} onSearch={runSearch} />

      {error && <p className="error">{error}</p>}

      {state && (
        <main className="results">
          <div className="results-bar">
            <span className="muted">
              Przejrzano {state.response.pdfCount} komunikatów z {state.years.length === 1 ? 'roku' : 'lat'}{' '}
              {[...state.years].sort().join(', ')}.
            </span>
            {state.response.results.length > 0 && (
              <button className="link-button" onClick={() => downloadCsv(state.response.results)}>
                Pobierz CSV
              </button>
            )}
          </div>

          {state.response.warnings.length > 0 && (
            <details className="notice">
              <summary>Nie udało się odczytać {state.response.warnings.length} plików</summary>
              <ul>
                {state.response.warnings.map((w) => (
                  <li key={w}>{w}</li>
                ))}
              </ul>
            </details>
          )}

          {state.names.map((name) => (
            <AthleteResults
              key={name}
              query={name}
              starts={state.response.results.filter((r) => r.query === name)}
              years={years}
              defaultYear={defaultYear}
            />
          ))}
        </main>
      )}

      <footer className="footer muted small">
        Dane pochodzą z <a href="https://agat.zlotoryja.pl/komunikaty/" target="_blank" rel="noreferrer">komunikatów na stronie klubu</a>.
        Sprawdź wyniki w oryginalnym PDF-ie, zanim złożysz wniosek.
      </footer>
    </div>
  )
}
