import type { Start } from './api'

const escape = (value: string | null | undefined) => {
  const s = value ?? ''
  return /[;"\n\r]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s
}

/** Wyniki jako CSV (separator ";", BOM — żeby Excel poprawnie pokazał polskie znaki). */
export function downloadCsv(results: Start[], fileName = 'starty.csv') {
  const header = ['Szukany', 'Data zawodów', 'Zawody', 'Dokument', 'Konkurencja', 'Miejsce', 'Zawodnik', 'Klub', 'Wynik', 'Szczegóły', 'PDF']
  const rows = results.map((r) => [
    r.query,
    r.date,
    r.eventName,
    r.documentTitle,
    r.competition,
    r.place,
    r.athlete,
    r.club,
    r.result,
    r.details.map((d) => (d.label ? `${d.label}: ${d.value}` : d.value)).join(', '),
    r.pdfUrl,
  ])
  const csv = [header, ...rows].map((row) => row.map(escape).join(';')).join('\r\n')
  const blob = new Blob(['﻿' + csv], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = fileName
  a.click()
  URL.revokeObjectURL(url)
}
