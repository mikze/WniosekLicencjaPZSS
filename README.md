# SzukajkaStartow

Aplikacja konsolowa (.NET 10), która przegląda komunikaty PDF z zawodów strzeleckich
(np. https://agat.zlotoryja.pl/komunikaty/komunikaty-2026/) i wypisuje starty wskazanych zawodników:
zawody, konkurencję, miejsce, klub i wynik.

## Uruchomienie

```bash
cd src/SzukajkaStartow
dotnet run -- "Jan Nowak"
```

Przykłady:

```bash
# kilku zawodników, zapis do CSV (otwiera się w Excelu)
dotnet run -- "Nowak Jan" "Kowalska Anna" --csv wyniki.csv

# konkretne lata
dotnet run -- -r 2025 -r 2026 "Nowak Jan"

# wszystkie lata podlinkowane na stronie (2018–2026), lista nazwisk z pliku
dotnet run -- -a -l zawodnicy.txt

# serie, składowe wieloboju i surowy wiersz z PDF-a
dotnet run -- "Jan Nowak" -v
```

Kolejność imienia i nazwiska, wielkość liter, przecinki i polskie znaki nie mają znaczenia
(`"nowak jan"` znajdzie `NOWAK, Jan`, a `"Łukasz Żak"` znajdzie `ZAK Lukasz`). Samo nazwisko też działa — wtedy program
ostrzega, jeśli pasuje kilka osób.

| Opcja | Opis |
|---|---|
| `-u, --url <adres>` | strona z komunikatami (można podać kilka razy) |
| `-r, --rok <RRRR>` | skrót do `…/komunikaty/komunikaty-RRRR/` |
| `-a, --wszystkie-lata` | przejrzyj też strony innych lat podlinkowane na stronie |
| `-l, --lista <plik>` | plik z zawodnikami, jeden na linię (`#` = komentarz) |
| `--csv <plik>` | eksport do CSV (`;`, UTF-8 z BOM) |
| `--cache <katalog>` | gdzie trzymać pobrane PDF-y (domyślnie `~/.local/share/SzukajkaStartow/cache`) |
| `--odswiez` | pobierz PDF-y ponownie |
| `-v, --szczegoly` | pokaż szczegóły i surowy wiersz z PDF-a |
| `-w, --wniosek <plik.pdf>` | uzupełnij wniosek o przedłużenie licencji (patrz niżej) |
| `--rok-wniosku <RRRR>` | z którego roku brać starty do wniosku (domyślnie bieżący) |
| `--licencja <lista>` | dyscypliny objęte licencją, np. `karabin` lub `pistolet,karabin` (domyślnie wykrywane) |
| `--wiodaca <dyscyplina>` | wymuś dyscyplinę wiodącą: pistolet / karabin / strzelba (domyślnie wybór automatyczny) |
| `--wzor`, `--czcionka` | inny plik wzoru (ten sam układ) / inna czcionka TTF |
| `--zrzut <plik.pdf/URL>` | diagnostyka: jak program widzi wiersze i kolumny w PDF-ie |

## Wniosek o przedłużenie licencji (PZSS)

```bash
dotnet run -- "Jan Nowak" --wniosek wniosek.pdf --rok-wniosku 2025
```

Program uzupełnia wzór wniosku PZSS ([Assets/](src/SzukajkaStartow/Assets/)) danymi, które zna z komunikatów:

- **Nazwisko i imię** (w zapisie z protokołów, wielkimi literami),
- **rok** („w 20__ roku”),
- **tabelę startów** — jeden wiersz na zawody (zawody i konsultacje z tego samego dnia to jeden wiersz):
  nazwa, data, miejsce oraz X w kolumnach pistolet / karabin / strzelba, zależnie od konkurencji, w których zawodnik startował.

**Wymagania PZSS:** 4 starty w dyscyplinie wiodącej i po 2 w każdej z pozostałych dyscyplin
**objętych licencją**. Przykłady: 4×pistolet + 2×karabin + 2×strzelba, 4×karabin + 2×pistolet + 2×strzelba,
a przy licencji na samą broń jednego rodzaju po prostu 4 starty w tej dyscyplinie.

- Program sam wybiera dyscyplinę wiodącą. Można ją wymusić opcją `--wiodaca karabin`.
- Zakres licencji podaje się opcją `--licencja karabin` albo `--licencja pistolet,karabin`. Bez niej program
  sprawdza pełną licencję. Jeśli warunki nie są spełnione, szuka największego zakresu, dla którego są,
  i pokazuje, czego brakuje do pełnej licencji.
- Gdy jeden wiersz na zawody nie wystarcza, konkurencje z tych samych zawodów są rozpisywane na osobne wiersze.
  Na przykład Psp i Pcz na „Wiosnie 2026” to 2 starty pistoletowe.

Przykładowy bilans i werdykt:

```
  Starty: pistolet (wiodąca) 5/4 ✓, karabin 2/2 ✓, strzelba 0 (poza licencją)
  ✅ SPEŁNIA warunki PZSS dla licencji: pistolet + karabin (4 starty w wiodącej: pistolet, po 2 w pozostałych).
     Dla licencji na wszystkie dyscypliny brakuje startów: strzelba 2.
```

Resztę (dyscypliny licencji, nr patentu i licencji, PESEL, telefon, posiadana broń, kolumna
„w kalendarzu WZSS/PZSS”, uwagi, podpisy) wypełnia się ręcznie. Starty z wynikiem DNS są pomijane.
Jeśli zawodów jest więcej niż 8, powstaje kolejna strona wniosku. Wtedy można usunąć zbędne wiersze
albo wydrukować tylko pierwszą stronę. Przy kilku zawodnikach powstaje osobny plik dla każdego
(`wniosek-NOWAK_JAN.pdf`).

Bez `-r`/`-u` przeszukiwana jest strona z roku podanego w `--rok-wniosku`.
Tekst jest wpisywany czcionką Liberation Sans (licencja SIL OFL, dołączona w `Assets`).

Pobrane PDF-y są zapisywane lokalnie, więc kolejne wyszukiwania są szybkie (cała strona 2026 ≈ 2–4 s).

Wersja jako jeden plik wykonywalny:

```bash
dotnet publish src/SzukajkaStartow -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true
# Windows: -r win-x64
```

## Jak to działa

1. **[WebScraper.cs](src/SzukajkaStartow/WebScraper.cs)** — czyta tabelę na stronie (data, nazwa zawodów, linki do PDF).
2. **[PdfCache.cs](src/SzukajkaStartow/PdfCache.cs)** — pobiera PDF-y i trzyma je w cache.
3. **[PdfLayout.cs](src/SzukajkaStartow/PdfLayout.cs)** — z pozycji słów w PDF-ie (biblioteka PdfPig) odtwarza wiersze i kolumny tabel.
4. **[ResultExtractor.cs](src/SzukajkaStartow/ResultExtractor.cs)** — śledzi bieżącą konkurencję i nagłówek tabeli,
   a wiersz z zawodnikiem rozkłada na miejsce / klub / wynik / szczegóły, dopasowując wartości do kolumn nagłówka.
5. **[Report.cs](src/SzukajkaStartow/Report.cs)** — wydruk w konsoli i CSV.
6. **[LicenseApplication.cs](src/SzukajkaStartow/LicenseApplication.cs)** — wiersze wniosku i nanoszenie tekstu na wzór PDF.

Obsługiwane formaty komunikatów (sprawdzone na latach 2018–2026):

- klubowe „Protokół nr X – konkurencja” (Miejsce / Nazwisko i imię / Klub / Wynik, także z seriami),
- wieloboje (Snajper, Trójbój — kolumny psp/pcz/ksp/kcz/SUMA),
- wyniki ISSF („REZULTATY”, Puchar Góry Mieszczańskiej) z wynikiem rozpisanym na kilka linii,
- IPSC („Match Results – Open/Production…”, Place Name / Club / Match Pts / Match %),
- strzelania dynamiczne (Pkt / Time / HF / %).

Obsada sędziowska i podpisy pod protokołami są pomijane, więc sędzia, który nie startował, nie pojawi się w wynikach.

## Ograniczenia

- PDF-y będące skanami (bez warstwy tekstowej) są zgłaszane jako ostrzeżenie i pomijane — wymagałyby OCR.
- Niektóre linki na stronie klubu są martwe (404) — program je zgłasza i idzie dalej.
- Przy nowym, nietypowym układzie protokołu część kolumn może nie dostać etykiety; wtedy `--zrzut` pokazuje, jak PDF został odczytany.
