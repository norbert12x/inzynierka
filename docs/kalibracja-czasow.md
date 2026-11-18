# Kalibracja szacunku czasów przejazdów

Data: 18.11.2026. Narzędzie: `GET /api/dev/routing/calibration` (`Planning/TravelCalibration.cs`). Wynik wpisany do `Routing:Estimate` w `Inżynierka/appsettings.json`.

## Po co

Planer bierze czasy między atrakcjami z macierzy OpenRouteService (`routing.travel_times`). Szacunek z odległości w linii prostej liczy tylko odcinki, których w macierzy nie ma: z i do noclegu (adresu nie wysyłamy do zewnętrznych usług), nowe atrakcje przed pobraniem macierzy i pary bez trasy. Kalibracja dopasowuje jego parametry do prawdziwych czasów po drogach w regionie.

## Dane

- 153 aktywne atrakcje (Rzeszów i okolice, promień 30 km).
- Samochód (`driving-car`): 23 064 pary (wszystkie pary w obu kierunkach, bez par bliższych niż 200 m).
- Pieszo (`foot-walking`): 3 344 pary do 3 km w linii prostej.
- Brak par bez trasy: ORS dotarł do każdej atrakcji.
- Porównanie bez parkowania, bo czas ORS go nie zawiera. W planie doliczamy osobno 5 min do każdego odcinka autem.

## Model i metoda

- Długość trasy = linia prosta (haversine) × współczynnik objazdu. Współczynnik to mediana ilorazu „trasa ORS ÷ linia prosta”.
- Jazda: czas = narzut + trasa ÷ prędkość. Narzut (wyraz wolny) i prędkość (z nachylenia) z regresji liniowej czasu ORS względem szacowanej trasy.
- Pieszo: czas = trasa ÷ prędkość, regresja przez zero.
- Błąd = szacunek − ORS (minuty). Podajemy medianę i 90. percentyl błędu bezwzględnego, medianę błędu w procentach i średni błąd ze znakiem (> 0 – szacunek zawyża).

## Parametry

| Parametr | Przed (założenie) | Po kalibracji |
|---|---|---|
| Współczynnik objazdu – auto | 1,35 | **1,42** |
| Prędkość – auto | 45 km/h | **51,4 km/h** |
| Narzut – auto | 0 min | **4,8 min** |
| Współczynnik objazdu – pieszo | 1,25 | **1,23** |
| Prędkość – pieszo | 4,5 km/h | **4,97 km/h** |
| Parkowanie | 5 min | 5 min (bez zmian, poza danymi ORS) |
| Próg spaceru | 15 min | 15 min (bez zmian) |

## Błąd szacunku względem ORS

### Samochód

| Odległość w linii prostej | Pary | Mediana błędu [min] przed → po | 90. percentyl [min] przed → po | Mediana błędu [%] przed → po | Średni błąd ze znakiem [min] przed → po |
|---|---|---|---|---|---|
| do 2 km | 2 372 | 2,1 → 2,6 | 3,8 → 3,9 | 55,6 → 66,9 | −2,2 → +2,4 |
| 2–10 km | 3 924 | 3,6 → **1,3** | 6,4 → 3,4 | 28,7 → 9,8 | −4,0 → −0,1 |
| powyżej 10 km | 16 768 | 4,1 → 4,1 | 12,6 → 11,9 | 11,2 → 10,4 | −2,1 → −0,5 |
| wszystkie | 23 064 | 3,6 → **3,0** | 11,1 → 10,2 | 15,3 → 11,5 | −2,4 → −0,1 |

### Pieszo

| Odległość w linii prostej | Pary | Mediana błędu [min] przed → po | 90. percentyl [min] przed → po | Mediana błędu [%] przed → po | Średni błąd ze znakiem [min] przed → po |
|---|---|---|---|---|---|
| do 1 km | 1 426 | 0,9 → 0,5 | 2,3 → 1,8 | 11,7 → 5,9 | +0,6 → −0,4 |
| 1–3 km | 1 918 | 4,4 → **1,5** | 6,9 → 4,1 | 15,4 → 5,4 | +3,7 → +0,1 |
| wszystkie | 3 344 | 2,2 → **0,9** | 6,3 → 3,3 | 14,1 → 5,6 | +2,4 → −0,1 |

## Wnioski

- Założone parametry jazdy **zaniżały** czasy (średnio o 2,4 min), a piesze **zawyżały** (o 2,4 min). Po kalibracji oba systematyczne błędy są bliskie zera.
- Największa poprawa jest dla jazdy 2–10 km: mediana błędu spadła z 3,6 do 1,3 min (z 29% do 10%). To typowa odległość z noclegu do pierwszej atrakcji, czyli główne zastosowanie szacunku.
- Krótkie przejazdy (do 2 km) są po kalibracji zawyżane o ok. 2,5 min. Wynika to z jednego stałego narzutu dla wszystkich odległości. Uznaliśmy to za akceptowalne: odcinki do ok. 1 km planer i tak każe przejść pieszo, a zawyżony czas daje luźniejszy, a nie niewykonalny plan.
- Dla odległości powyżej 10 km 90. percentyl to ok. 12 min. Linia prosta nie oddaje krętych dróg i braku przepraw. Dla par atrakcji planer używa czasów ORS, więc ten błąd dotyczy tylko dalekich dojazdów z noclegu.
- Szacunek pieszy po kalibracji myli się zwykle o mniej niż minutę (mediana 0,9 min, 5,6%).

## Ograniczenia

- Kalibracja na parach atrakcji (głównie centrum Rzeszowa i okoliczne miejscowości). Nocleg może leżeć gdzie indziej, np. na wsi z gorszym dojazdem.
- ORS nie uwzględnia korków, a jego czasy jazdy są ogólnie dłuższe niż w Google Maps (np. Rynek Rzeszów → Zamek Łańcut: Google 27 min). Dlatego czasy w planie są orientacyjne, a nawigację prowadzi Google Maps.
- Po większych zmianach katalogu (nowe obszary, dużo nowych atrakcji) kalibrację warto powtórzyć tym samym narzędziem.
