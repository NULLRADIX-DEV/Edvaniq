# Sortierverfahren

Sortierte Daten lassen sich schneller durchsuchen und übersichtlicher darstellen. Ein Sortierverfahren bringt die Elemente einer Liste in eine Reihenfolge, zum Beispiel Zahlen aufsteigend oder Namen alphabetisch.

## Bubblesort

Definition: Bubblesort geht die Liste immer wieder von vorn nach hinten durch und vertauscht zwei benachbarte Elemente, wenn sie in der falschen Reihenfolge stehen. Nach jedem Durchgang steht das größte noch unsortierte Element am Ende, es steigt auf wie eine Blase.

Bubblesort ist leicht zu verstehen, aber langsam. Bei n Elementen braucht es bis zu n - 1 Durchgänge mit jeweils bis zu n - 1 Vergleichen, also ungefähr n² Vergleiche.

```python
def bubblesort(liste):
    n = len(liste)
    for durchgang in range(n - 1):
        for i in range(n - 1 - durchgang):
            if liste[i] > liste[i + 1]:
                liste[i], liste[i + 1] = liste[i + 1], liste[i]
    return liste
```

## Sortieren durch Einfügen

Definition: Sortieren durch Einfügen (Insertionsort) baut die sortierte Liste Element für Element auf. Jedes neue Element wird an der richtigen Stelle in den schon sortierten Teil eingefügt, so wie man Spielkarten auf der Hand ordnet.

Ist die Liste schon fast sortiert, ist das Verfahren sehr schnell, weil kaum ein Element weit wandern muss. Im schlechtesten Fall, bei einer umgekehrt sortierten Liste, braucht es wie Bubblesort ungefähr n² / 2 Vergleiche.

## Mergesort

Definition: Mergesort arbeitet nach dem Prinzip „Teile und herrsche“. Es teilt die Liste in zwei Hälften, sortiert jede Hälfte für sich auf dieselbe Weise und fügt die beiden sortierten Hälften dann zu einer sortierten Liste zusammen. Das Zusammenfügen heißt Mischen (englisch merge).

Die Laufzeit T(n) für n Elemente ergibt sich aus zwei halb so großen Teilaufgaben und dem Mischen, das etwa n Schritte braucht: T(n) = 2 · T(n / 2) + n. Daraus folgt eine Laufzeit von ungefähr n · log2(n) Schritten. Bei einer Million Elementen sind das etwa 20 Millionen Schritte statt einer Billion bei Bubblesort.

Ein Nachteil von Mergesort ist der zusätzliche Speicher: Beim Mischen braucht es Platz für eine zweite Liste.

## Stabilität

Definition: Ein Sortierverfahren heißt stabil, wenn Elemente mit gleichem Sortierschlüssel nach dem Sortieren in derselben Reihenfolge stehen wie vorher.

Stabilität ist wichtig, wenn man nacheinander nach mehreren Merkmalen sortiert. Sortiert man eine Klassenliste erst nach Vornamen und dann stabil nach Nachnamen, stehen Personen mit gleichem Nachnamen danach alphabetisch nach Vornamen. Bubblesort, Sortieren durch Einfügen und Mergesort sind stabil.
