import { useEffect, useRef, useState } from 'react'
import { getYears, search, type SearchResponse } from './api'
import { AthleteResults } from './components/AthleteResults'
import { CompareView } from './components/CompareView'
import { SearchForm } from './components/SearchForm'
import { downloadCsv } from './csv'

const CURRENT_YEAR = new Date().getFullYear()

type Tab = 'szukaj' | 'porownanie'
const tabFromHash = (): Tab => (window.location.hash === '#porownanie' ? 'porownanie' : 'szukaj')

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
  const [tab, setTab] = useState<Tab>(tabFromHash)

  // Zakładka zapisana w adresie (#porownanie) — działa przycisk Wstecz i można wysłać link.
  useEffect(() => {
    const onHash = () => setTab(tabFromHash())
    window.addEventListener('hashchange', onHash)
    return () => window.removeEventListener('hashchange', onHash)
  }, [])
  const openTab = (t: Tab) => {
    window.location.hash = t === 'porownanie' ? 'porownanie' : ''
    setTab(t)
  }

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
          Starty zawodników z komunikatów KS AGAT Złotoryja: wniosek PZSS o przedłużenie licencji i porównanie wyników.
        </p>
      </header>

      <nav className="tabs" role="tablist" aria-label="Widok">
        <button role="tab" aria-selected={tab === 'szukaj'} className={tab === 'szukaj' ? 'tab active' : 'tab'} onClick={() => openTab('szukaj')}>
          Wniosek PZSS i starty
        </button>
        <button
          role="tab"
          aria-selected={tab === 'porownanie'}
          className={tab === 'porownanie' ? 'tab active' : 'tab'}
          onClick={() => openTab('porownanie')}
        >
          Porównanie
        </button>
      </nav>

      <div role="tabpanel" hidden={tab !== 'porownanie'}>
        <CompareView years={years} />
      </div>

      <div role="tabpanel" hidden={tab !== 'szukaj'}>
        <section className="card intro">
          <p>
            <strong>Wygeneruj wniosek PZSS o przedłużenie licencji zawodniczej</strong> na podstawie startów z komunikatów
            klubu. Program sam sprawdzi, czy zawodnik spełnia warunki (4 starty w dyscyplinie wiodącej i po 2 w pozostałych),
            i uzupełni tabelę zawodów we wniosku.
          </p>
          <ol className="steps">
            <li>
              <strong>Wpisz zawodnika</strong>
              <span>imię i nazwisko, wybierz rok</span>
            </li>
            <li>
              <strong>Kliknij „Wniosek PZSS”</strong>
              <span>przy nazwisku zobaczysz, czy spełnia warunki</span>
            </li>
            <li>
              <strong>Sprawdź podgląd i pobierz PDF</strong>
              <span>PESEL, nr licencji i podpis dopisz ręcznie</span>
            </li>
          </ol>
        </section>

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
                licenseOpenInitially={state.names.length === 1}
              />
            ))}
          </main>
        )}
      </div>

      <footer className="footer muted small">
        Dane pochodzą z <a href="https://agat.zlotoryja.pl/komunikaty/" target="_blank" rel="noreferrer">komunikatów na stronie klubu</a>.
        Sprawdź wyniki w oryginalnym PDF-ie, zanim złożysz wniosek.
      </footer>
    </div>
  )
}
