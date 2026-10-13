# Struktura plików CSV

Dokumentacja odpowiada odczytowi danych w programie. Każdą operację uruchamiaj z jej własnym plikiem CSV. Nazwy, adresy IP, numery PROFINET, sieci i PLC w przykładach należy dopasować do projektu.

## Zasady dla importu urządzeń

Pierwszy wiersz zawiera nagłówki, kolejne — po jednym urządzeniu. Zalecany separator: średnik. GUI obsługuje także przecinek i tabulator. Nazwy kolumn nie rozróżniają wielkości liter. Puste wiersze są pomijane. Używaj prostych pól bez separatorów i cudzysłowów wewnątrz wartości — import nie obsługuje pełnego standardu CSV z cytowaniem pól. Zalecany zapis: UTF-8 bez BOM (parser Hikvision obsługuje również BOM).

Liczby wpisuj jako całkowite wartości dziesiętne, np. `1000`, bez `IB`, `QB` i jednostek. Dla Lenze, Pepperl i Keyence brak lub niepoprawna liczba może dać wartość 0; podawaj liczby jawnie.

## Lenze — CSV → Lenze

[Przykładowy plik](../examples/lenze.csv)

```csv
Name;DeviceName;Type;IpAddress;SubnetName;ProfinetNumber;PlcName;StartAddress
lenze01;lenze01_DEV;UZUPELNIJ_TYPE_Z_KATALOGU_TIA;192.168.0.101;PN;101;PLC1;1000
```

| Kolumna | Co wpisać |
| --- | --- |
| `Name` | Nazwa elementu urządzenia, np. `lenze01`. Wymagana. |
| `DeviceName` | Nazwa urządzenia w projekcie, np. `lenze01_DEV`. Wymagana. |
| `Type` | Pełny identyfikator katalogowy TIA dla konkretnego napędu. Brak wartości domyślnej. Zastąp tekst przykładu rzeczywistym identyfikatorem; zwykła nazwa modelu nie wystarczy. |
| `IpAddress` | Adres IPv4 napędu. |
| `SubnetName` | Dokładna nazwa istniejącej podsieci w projekcie. |
| `ProfinetNumber` | Numer urządzenia PROFINET w systemie IO. |
| `PlcName` | Nazwa sterownika używana do wyboru systemu IO. Gdy nie zostanie znaleziony, program wybiera pierwszy system IO w podsieci. |
| `StartAddress` | Baza adresacji napędu dla danego wiersza. Obecne mapowanie ustawia wskazane moduły na bazę, bazę +2 i bazę +4; wymaga odpowiedniej struktury urządzenia Lenze. |

Puste `Name` lub `DeviceName`, albo wartość `0` w którymś z tych pól kończy import dalszych wierszy. `GroupName` jest odczytywane, ale nie jest używane do przypisania grupy.

## Pepperl+Fuchs — CSV → Master (Pepperl)

[Przykładowy plik ze wszystkimi 10 slotami](../examples/pepperl.csv)

| Kolumna | Co wpisać |
| --- | --- |
| `Name` | Nazwa mastera. `0` kończy listę; wpisz niepustą nazwę. |
| `Type` | Można pozostawić puste: domyślnie ICE38IOL, `GSD:GSDML-V2.35-PEPPERL+FUCHS-ICE38IOL-AMS-20250201.XML/DAP/DAP4`. |
| `IpAddress` | Adres IPv4 mastera. |
| `SubnetName` | Dokładna nazwa istniejącej podsieci. |
| `ProfinetNumber` | Numer urządzenia PROFINET. |
| `PlcName` | Nazwa PLC do wyboru systemu IO; przy braku dopasowania wybierany jest pierwszy system IO. |
| `StartAddress` | Baza adresów danego mastera. Sposób rozstawienia slotów wybierasz w GUI. |
| `Slot1Name` … `Slot10Name` | Nazwa modułu w konkretnym slocie. Dla używanego slotu wpisz rzeczywistą nazwę, np. `master01_G20`. Puste lub `0` daje nazwę `NAZWA_MASTERA_n_EMPTY` i powoduje traktowanie slotu jako pustego przy adresacji. |
| `Slot1Type` … `Slot10Type` | Identyfikator modułu GSDML dla konkretnego slotu. `G20` jest obsługiwanym skrótem dla `GSD:GSDML-V2.35-PEPPERL+FUCHS-ICE38IOL-AMS-20250201.XML/M/ID_Mod_G20_984229`. Puste lub `0` tworzy moduł pusty. Dla innych modułów użyj pełnego identyfikatora `/M/...` z właściwego GSDML. |

Pary pól slotów powtarzają się dla numerów 1–10. Pominięte kolumny slotów są traktowane jak puste. `DeviceName` nie steruje nazwą urządzenia — program tworzy ją jako `Name_DEV` (z możliwą korektą przy kolizji). `GroupName` nie przypisuje mastera do grupy.

Tryby adresacji:

- Sekwencyjny: zgodnie z rozmiarem danych modułów.
- Co 100: sloty 1–9 od bazy +100… +900; slot 10 za danymi slotu 9.
- Co 32: sloty 1–10 od bazy do bazy +288. Każdy slot rezerwuje 32 bajty I i 32 bajty Q, także pusty. Moduł większy niż 32 bajty w jednym kierunku jest pomijany z błędem w logu.

## Keyence — CSV → Kamera SR-X1H3HX

[Przykładowy plik](../examples/keyence.csv)

| Kolumna | Co wpisać |
| --- | --- |
| `Name` | Nazwa elementu kamery. Puste lub `0` kończy listę. |
| `DeviceName` | Nazwa urządzenia. Puste lub `0` daje `Name_DEV`. |
| `Type` | Puste wybiera `GSD:GSDML-V2.43-KEYENCE-SR-X1H3H-20240716.XML/DAP/DAP1`. Wpisane `0` nie wybiera wartości domyślnej — pozostaw pole puste. |
| `IpAddress` | Adres IPv4 kamery. |
| `SubnetName` | Nazwa istniejącej podsieci; puste wybiera pierwszą podsieć. |
| `ProfinetNumber` | Numer urządzenia PROFINET. |
| `PlcName` | Nazwa PLC do wyboru systemu IO; przy braku dopasowania wybierany jest pierwszy system IO. |
| `StartAddress` | Baza I oraz Q dla każdej kamery osobno. Kolejne obszary są rozstawiane według długości danych odczytanych z TIA. Zaplanuj osobne zakresy dla kolejnych kamer. |

`GroupName` jest odczytywane, ale nie przypisuje kamery Keyence do grupy. GSDML musi być zainstalowany w TIA.

## Hikvision — CSV → hik 3050

[Przykładowy plik](../examples/hik3050.csv) · [Szczegółowa instrukcja](../HIK_3050.md)

| Kolumna | Co wpisać |
| --- | --- |
| `Name` | Nazwa elementu DAP kamery. Puste lub `0` kończy listę. |
| `DeviceName` | Nazwa urządzenia; puste lub `0` daje `Name_DEV`. |
| `Type` | Puste lub `0` wybiera `GSD:GSDML-V2.34-HIKVISION-MV-20210719.XML/DAP/DAP 3`. |
| `IpAddress` | Poprawny adres IPv4. |
| `SubnetName` | Nazwa podsieci; puste wybiera pierwszą. |
| `ProfinetNumber` | Dodatni numer urządzenia PROFINET. |
| `PlcName` | Nazwa PLC; przy braku dopasowania wybierany jest pierwszy system IO. |
| `GroupName` | Grupa urządzeń na najwyższym poziomie projektu; brakująca grupa jest tworzona. Puste lub `0` pozostawia urządzenie bez grupy. |
| `StartAddress` | Wypełnij w pierwszym wierszu urządzenia: wspólna baza I i Q dla całej listy. W kolejnych wierszach pozostaw puste — wartości są ignorowane. |

Import szuka kolejnych wolnych zakresów, omijając zajęte adresy w projekcie, oddzielnie dla I i Q. Nie trzeba wyliczać bazy każdej kamery. Błędny IP lub wymagany numer/adres przerywa import. Istniejąca nazwa urządzenia także przerywa import.

## Generacja LAD — CSV → Conveyor (LAD)

[Przykładowy plik](../examples/conveyor-lad.csv)

Ten plik ma **jedną kolumnę**: każdy wiersz to pełna nazwa napędu. Nie rozdzielaj numeru i typu średnikiem. Nagłówek `Drive` jest opcjonalny i zostaje pominięty jako wiersz niepasujący do wzorca.

```csv
Drive
M0880-F
M0881-R
M0882-FH
M0900-IR
M0901-IB
M0902-IL
```

| Fragment nazwy | Znaczenie |
| --- | --- |
| `M` + 1–4 cyfry | Numer napędu, np. `M0881`. Program normalizuje go do czterech cyfr. |
| `-R` | Przenośnik rolkowy (motor roller). |
| `-F` lub `-FH` | Przenośnik obsługiwany jako napęd falownikowy. |
| `-IR` | Część rolkowa grupy FRAT. |
| `-IB` | Część pasowa grupy FRAT. |
| `-IL` | Część podnosząca grupy FRAT. |

FRAT wymaga kompletu kolejnych numerów: `M0900-IR`, `M0901-IB`, `M0902-IL`. Niepełne grupy nie tworzą kompletnej instancji FRAT. Kolejność wierszy wpływa na kolejność elementów i połączenia sąsiednich przenośników. Puste i niepasujące wiersze są pomijane; brak poprawnych przenośników lub grup FRAT powoduje błąd.

Oprócz CSV wskaż w GUI zgodny szablon XML bloku FB wyeksportowany z TIA. Musi zawierać odpowiednie sieci LAD i deklaracje dla generowanych elementów. CSV nie definiuje bloków, tagów ani stałych wymaganych przez szablon — muszą pasować do projektu docelowego.