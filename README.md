# TIA Openness Automation Tool

Aplikacja Windows z graficznym interfejsem do automatyzacji pracy w **Siemens TIA Portal V19** przez API Openness. Pomaga dodawać urządzenia z CSV, konfigurować adresację i wykonywać powtarzalne operacje w projekcie.

## Pobieranie

Gotowe wydania i paczki programu znajdziesz w [Releases](https://github.com/ssshoes/tia-openness-automation-tool/releases). Do uruchomienia gotowej aplikacji **nie potrzebujesz Visual Studio**.

## Co robi program?

- Importuje napędy Lenze i mastery Pepperl+Fuchs z CSV.
- Importuje kamery Keyence SR-X1H3HX oraz Hikvision hik 3050.
- Pozwala wybrać sposób adresacji masterów Pepperl w GUI.
- Generuje logikę przenośników w LAD z CSV i szablonu XML.
- Eksportuje bloki PLC do XML.
- Obsługuje uruchamianie sesji TIA Multiuser i otwieranie lokalnych sesji.
- Konfiguruje zabezpieczenia PLC i role UMAC.

## Tryby adresacji Master (Pepperl)

| Tryb | Działanie |
| --- | --- |
| Sekwencyjny | Kolejne adresy zależą od rozmiaru danych modułów i kierunku I/Q. |
| Blokowy co 100 | Sloty 1–9: baza +100, +200… +900. Slot 10 kontynuuje za slotem 9. |
| Blokowy co 64 | Każdy slot ma zakres 64 bajtów wejść I i 64 bajtów wyjść Q. Początki: baza +64, +128… +640. Puste sloty zachowują swój zakres. |

Adres bazowy pochodzi z `StartAddress` w CSV. Przykład dla bazy 1000 w trybie co 64: slot 1 zaczyna I/Q od 1064, slot 2 od 1128, a slot 10 od 1640. Moduł wymagający więcej niż 64 bajtów w jednym kierunku jest pomijany z komunikatem błędu.

## Wymagania do uruchomienia

- Windows 64-bit.
- .NET Framework 4.8.
- Siemens TIA Portal V19 z zainstalowanym TIA Portal Openness.
- Konto użytkownika w grupie Windows `Siemens TIA Openness`; po dodaniu do grupy należy wylogować się i zalogować ponownie.
- Odpowiednie urządzenia/GSDML zainstalowane w katalogu TIA dla wybranego importu.

## Uruchomienie paczki z Releases

1. Pobierz ZIP i rozpakuj go do jednego folderu.
2. Z własnej instalacji TIA Portal V19 skopiuj do tego folderu `Siemens.Engineering.dll` i `Siemens.Engineering.Hmi.dll`. Standardowa lokalizacja: `C:\Program Files\Siemens\Automation\Portal V19\PublicAPI\V19`.
3. Uruchom `TIA_Project_v11.exe`.
4. Wybierz operację, wskaż CSV oraz projekt lub sesję Multiuser i uzupełnij wymagane parametry.
5. Dla importu Pepperl wybierz `Tryb adresacji Master`, a następnie kliknij **WYKONAJ**. Wyniki operacji są widoczne w logu.

Paczka zawiera aplikację i konfigurację. Biblioteki Siemens należy pobrać z własnej instalacji TIA. Samo EXE nie stanowi samodzielnej instalacji TIA Portal.

Szczegóły importu Hikvision: [HIK_3050.md](HIK_3050.md). Przykładowe dane: [hik_3050_example.csv](hik_3050_example.csv).

## Budowanie ze źródeł

Otwórz `TIA_Project_v11.sln` w Visual Studio z obsługą aplikacji desktopowych .NET i zainstalowanym .NET Framework 4.8 Developer Pack. Zbuduj projekt dla platformy `x64`.

Projekt odwołuje się do bibliotek Openness w domyślnej lokalizacji TIA Portal V19. Jeśli instalacja jest w innym miejscu, dostosuj `HintPath` w `TIA_Project_v11.csproj`.

## Hasła do konfiguracji PLC

Przed użyciem opcji PLC Security ustaw lokalnie zmienne środowiskowe:

- `TIA_AMSORT_USER_PASSWORD`
- `TIA_OPC_UA_USER_PASSWORD`

Wartości haseł nie są przechowywane w repozytorium.

## Pliki CSV — wzorce dla każdego zastosowania

Dokumentacja odpowiada odczytowi danych w programie. Każdą operację uruchamiaj z jej własnym plikiem CSV. Nazwy, adresy IP, numery PROFINET, sieci i PLC w przykładach należy dopasować do projektu.

### Zasady dla importu urządzeń

Pierwszy wiersz zawiera nagłówki, kolejne — po jednym urządzeniu. Zalecany separator: średnik. GUI obsługuje także przecinek i tabulator. Nazwy kolumn nie rozróżniają wielkości liter. Puste wiersze są pomijane. Używaj prostych pól bez separatorów i cudzysłowów wewnątrz wartości — import nie obsługuje pełnego standardu CSV z cytowaniem pól. Zalecany zapis: UTF-8 bez BOM (parser Hikvision obsługuje również BOM).

Liczby wpisuj jako całkowite wartości dziesiętne, np. `1000`, bez `IB`, `QB` i jednostek. Dla Lenze, Pepperl i Keyence brak lub niepoprawna liczba może dać wartość 0; podawaj liczby jawnie.

### Lenze — CSV → Lenze

[Przykładowy plik](examples/lenze.csv)

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

### Pepperl+Fuchs — CSV → Master (Pepperl)

[Przykładowy plik ze wszystkimi 10 slotami](examples/pepperl.csv)

```csv
Name;Type;IpAddress;SubnetName;ProfinetNumber;PlcName;StartAddress;Slot1Name;Slot1Type;Slot2Name;Slot2Type;Slot3Name;Slot3Type;Slot4Name;Slot4Type;Slot5Name;Slot5Type;Slot6Name;Slot6Type;Slot7Name;Slot7Type;Slot8Name;Slot8Type;Slot9Name;Slot9Type;Slot10Name;Slot10Type
master01;;192.168.0.121;PN;121;PLC1;3000;master01_G20;G20;0;0;0;0;0;0;0;0;0;0;0;0;0;0;0;0;0;0
```

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
- Co 64: sloty 1–10 od bazy +64… +640. Każdy slot rezerwuje 64 bajty I i 64 bajty Q, także pusty. Moduł większy niż 64 bajty w jednym kierunku jest pomijany z błędem w logu.

### Keyence — CSV → Kamera SR-X1H3HX

[Przykładowy plik](examples/keyence.csv)

```csv
Name;DeviceName;Type;IpAddress;SubnetName;ProfinetNumber;PlcName;StartAddress
keyence01;keyence01_DEV;;192.168.0.111;PN;111;PLC1;2000
```

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

### Hikvision — CSV → hik 3050

[Przykładowy plik](examples/hik3050.csv) · [Szczegółowa instrukcja](HIK_3050.md)

```csv
Name;DeviceName;Type;IpAddress;SubnetName;ProfinetNumber;PlcName;GroupName;StartAddress
hik-3050-01;hik-3050-01_DEV;GSD:GSDML-V2.34-HIKVISION-MV-20210719.XML/DAP/DAP 3;192.168.0.101;;101;;Kamery;1000
hik-3050-02;hik-3050-02_DEV;GSD:GSDML-V2.34-HIKVISION-MV-20210719.XML/DAP/DAP 3;192.168.0.102;;102;;Kamery;
hik-3050-03;hik-3050-03_DEV;GSD:GSDML-V2.34-HIKVISION-MV-20210719.XML/DAP/DAP 3;192.168.0.103;;103;;Kamery;
```

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

### Generacja LAD — CSV → Conveyor (LAD)

[Przykładowy plik](examples/conveyor-lad.csv)

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

### Pozostałe operacje

Eksport bloków XML, konfiguracja PLC Security i obsługa sesji Multiuser nie wymagają pliku CSV. Ich parametry podaje się w GUI; hasła dla PLC Security ustawia się w zmiennych środowiskowych opisanych wyżej.
