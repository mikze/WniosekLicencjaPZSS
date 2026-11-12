import { useEffect, useRef, useState } from 'react'
import * as pdfjs from 'pdfjs-dist'
import workerUrl from 'pdfjs-dist/build/pdf.worker.min.mjs?url'

// Ten moduł jest ładowany dopiero przy otwarciu podglądu (React.lazy), więc pdf.js nie spowalnia strony.
pdfjs.GlobalWorkerOptions.workerSrc = workerUrl

interface Props {
  pdf: Blob
}

/** Rysuje strony PDF-a na canvasach na szerokość kontenera — działa też na telefonach, gdzie iframe z PDF-em nie działa. */
export default function PdfPreview({ pdf }: Props) {
  const containerRef = useRef<HTMLDivElement>(null)
  const [width, setWidth] = useState(0)
  const [error, setError] = useState<string | null>(null)

  // Szerokość kontenera (zaokrąglona, żeby drobne zmiany nie rysowały wszystkiego od nowa).
  useEffect(() => {
    const el = containerRef.current
    if (!el) return
    const observer = new ResizeObserver(([entry]) => setWidth(Math.round(entry.contentRect.width / 20) * 20))
    observer.observe(el)
    return () => observer.disconnect()
  }, [])

  useEffect(() => {
    const el = containerRef.current
    if (!el || width === 0) return
    let cancelled = false
    let task: pdfjs.PDFDocumentLoadingTask | null = null

    ;(async () => {
      try {
        task = pdfjs.getDocument({ data: new Uint8Array(await pdf.arrayBuffer()) })
        const doc = await task.promise
        const canvases: HTMLCanvasElement[] = []
        for (let n = 1; n <= doc.numPages; n++) {
          const page = await doc.getPage(n)
          const base = page.getViewport({ scale: 1 })
          const ratio = window.devicePixelRatio || 1
          const viewport = page.getViewport({ scale: (width / base.width) * ratio })
          const canvas = document.createElement('canvas')
          canvas.width = Math.floor(viewport.width)
          canvas.height = Math.floor(viewport.height)
          canvas.style.width = '100%'
          canvas.setAttribute('aria-label', `Strona ${n} z ${doc.numPages}`)
          await page.render({ canvas, viewport }).promise
          if (cancelled) return
          canvases.push(canvas)
        }
        if (!cancelled) {
          el.replaceChildren(...canvases)
          setError(null)
        }
      } catch (e) {
        if (!cancelled) setError((e as Error).message)
      }
    })()

    return () => {
      cancelled = true
      void task?.destroy()
    }
  }, [pdf, width])

  return (
    <>
      {error && <p className="error">Nie udało się wyświetlić podglądu: {error}</p>}
      <div ref={containerRef} className="pdf-pages" />
    </>
  )
}
