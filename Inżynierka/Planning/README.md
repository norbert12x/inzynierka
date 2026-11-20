# Planning – logika planera

Czyste klasy C#: dostają dane (atrakcje, czasy przejazdów, godziny dni, noclegi) i zwracają plan. Bez `DbContext`, `HttpClient` i konfiguracji – dane pobiera kontroler/serwis i przekazuje tutaj. Dzięki temu każdą regułę testujemy w `Inżynierka.Tests` na wymyślonych danych.

Zakres: epik E4 w `docs/plan-prac.md`.

Plan dnia (E4.2): `RouteOptimizer` (kolejność) → `DayScheduler` (godziny, czekanie na otwarcie, posiłek, powrót) → `DayPlanner` (całość + usuwanie opcjonalnych). Wiele dni (E4.3): `TripPlanner` (przydział do dni, potem `DayPlanner`). Pełne dni w pierwszym planie (E4.7): `TripFiller`; propozycje (E4.5): `SuggestionFinder`; wspólne wstawianie: `Insertion`. Model wejścia i wyniku: `PlanModel.cs`.
