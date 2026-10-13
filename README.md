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

## Pliki CSV — kolumny i przykłady

Pełne opisy pól dla Lenze, Pepperl, Keyence, Hikvision oraz generatora LAD znajdziesz w [instrukcji CSV](docs/CSV.md). Gotowe pliki do uzupełnienia są w folderze [examples](examples).
