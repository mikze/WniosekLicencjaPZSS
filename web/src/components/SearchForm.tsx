import { useState, type FormEvent } from 'react'

interface Props {
  years: number[]
  loading: boolean
  onSearch: (names: string[], years: number[]) => void
}

export function SearchForm({ years, loading, onSearch }: Props) {
  const [namesText, setNamesText] = useState('')
  const [selected, setSelected] = useState<number[] | null>(null)

  // Domyślnie zaznaczony najnowszy rok, dopóki użytkownik sam czegoś nie wybierze.
  const selectedYears = selected ?? (years.length > 0 ? [years[0]] : [])
  const names = namesText.split('\n').map((n) => n.trim()).filter(Boolean)

  const toggleYear = (year: number) =>
    setSelected(selectedYears.includes(year) ? selectedYears.filter((y) => y !== year) : [...selectedYears, year])

  const submit = (e: FormEvent) => {
    e.preventDefault()
    if (names.length > 0 && selectedYears.length > 0) onSearch(names, selectedYears)
  }

  return (
    <form className="card search-form" onSubmit={submit}>
      <label className="field">
        <span className="field-label">Zawodnicy</span>
        <textarea
          rows={3}
          value={namesText}
          onChange={(e) => setNamesText(e.target.value)}
          placeholder={'Jan Nowak\nKowalska Anna'}
          spellCheck={false}
        />
        <span className="hint">Jeden zawodnik w linii. Kolejność imienia i nazwiska ani polskie znaki nie mają znaczenia.</span>
      </label>

      <fieldset className="field">
        <legend className="field-label">Lata</legend>
        <div className="chips">
          {years.length === 0 && <span className="hint">Wczytuję listę lat…</span>}
          {years.map((y) => (
            <button
              key={y}
              type="button"
              className={`chip ${selectedYears.includes(y) ? 'chip-on' : ''}`}
              aria-pressed={selectedYears.includes(y)}
              onClick={() => toggleYear(y)}
            >
              {y}
            </button>
          ))}
          {years.length > 1 && (
            <button
              type="button"
              className="link-button"
              onClick={() => setSelected(selectedYears.length === years.length ? [years[0]] : years)}
            >
              {selectedYears.length === years.length ? 'tylko najnowszy' : 'wszystkie'}
            </button>
          )}
        </div>
      </fieldset>

      <div className="form-actions">
        <button className="primary" type="submit" disabled={loading || names.length === 0 || selectedYears.length === 0}>
          {loading ? 'Szukam…' : 'Szukaj'}
        </button>
        {loading && (
          <span className="hint">
            Przeglądam komunikaty PDF. Pierwsze wyszukiwanie dla danego roku może potrwać kilkanaście sekund.
          </span>
        )}
      </div>
    </form>
  )
}
