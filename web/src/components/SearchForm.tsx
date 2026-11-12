import { useState, type FormEvent } from 'react'

interface Props {
  years: number[]
  loading: boolean
  onSearch: (names: string[], years: number[]) => void
  submitLabel?: string
  placeholder?: string
  /** Minimalna i maksymalna liczba zawodników (np. porównanie wymaga co najmniej dwóch). */
  minNames?: number
  maxNames?: number
}

export function SearchForm({
  years,
  loading,
  onSearch,
  submitLabel = 'Szukaj',
  placeholder = 'Jan Nowak\nKowalska Anna',
  minNames = 1,
  maxNames = 20,
}: Props) {
  const [namesText, setNamesText] = useState('')
  const [selected, setSelected] = useState<number[] | null>(null)

  // Domyślnie zaznaczony najnowszy rok, dopóki użytkownik sam czegoś nie wybierze.
  const selectedYears = selected ?? (years.length > 0 ? [years[0]] : [])
  const names = namesText.split('\n').map((n) => n.trim()).filter(Boolean)

  const namesOk = names.length >= minNames && names.length <= maxNames

  const toggleYear = (year: number) =>
    setSelected(selectedYears.includes(year) ? selectedYears.filter((y) => y !== year) : [...selectedYears, year])

  const submit = (e: FormEvent) => {
    e.preventDefault()
    if (namesOk && selectedYears.length > 0) onSearch(names, selectedYears)
  }

  return (
    <form className="card search-form" onSubmit={submit}>
      <label className="field">
        <span className="field-label">Zawodnicy</span>
        <textarea
          rows={Math.min(8, Math.max(3, namesText.split('\n').length + 1))}
          value={namesText}
          onChange={(e) => setNamesText(e.target.value)}
          placeholder={placeholder}
          spellCheck={false}
        />
        <span className="hint">
          Jeden zawodnik w linii{minNames > 1 ? ` (od ${minNames} do ${maxNames})` : ''}. Kolejność imienia i nazwiska ani
          polskie znaki nie mają znaczenia.
        </span>
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
        <button className="primary" type="submit" disabled={loading || !namesOk || selectedYears.length === 0}>
          {loading ? 'Szukam…' : submitLabel}
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
