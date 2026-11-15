# Komplexität von Algorithmen

Zwei Programme können dieselbe Aufgabe lösen und trotzdem sehr unterschiedlich lange brauchen. Die Komplexität beschreibt, wie der Aufwand eines Algorithmus wächst, wenn die Eingabe größer wird.

## Laufzeit und Schrittzahl

Definition: Die Laufzeit eines Algorithmus gibt man als Zahl der Grundschritte an, die er für eine Eingabe der Größe n braucht. Grundschritte sind zum Beispiel Vergleiche, Zuweisungen oder Rechenoperationen.

Man zählt Schritte statt Sekunden, weil Sekunden vom Rechner abhängen. Meist betrachtet man den schlechtesten Fall, also die Eingabe, bei der der Algorithmus am längsten braucht. Für die lineare Suche ist das ein Wert, der nicht in der Liste steht: Sie braucht dann n Vergleiche.

## O-Notation

Definition: Eine Funktion f(n) liegt in O(g(n)), wenn es eine Konstante c und eine Startgröße n0 gibt, sodass f(n) ≤ c · g(n) für alle n ≥ n0 gilt.

Die O-Notation beschreibt also nur, wie schnell der Aufwand wächst, und lässt konstante Faktoren und kleine Glieder weg. Braucht ein Algorithmus 3n² + 5n + 2 Schritte, liegt er in O(n²): Für große n bestimmt das Glied n² den Aufwand, die übrigen fallen kaum ins Gewicht.

## Komplexitätsklassen

Häufige Komplexitätsklassen, vom schnellsten zum langsamsten Wachstum:

- O(1), konstant: der Zugriff auf ein Listenelement über seinen Index.
- O(log n), logarithmisch: die binäre Suche.
- O(n), linear: die lineare Suche.
- O(n · log n): Mergesort.
- O(n²), quadratisch: Bubblesort und Sortieren durch Einfügen im schlechtesten Fall.
- O(2^n), exponentiell: das Ausprobieren aller Teilmengen einer Menge.

Der Unterschied wird bei großen Eingaben riesig. Für n = 1000 ist log2(n) etwa 10, n · log2(n) etwa 10 000 und n² eine Million. Für n = 100 hat 2^n schon mehr als 30 Stellen.

## Speicherbedarf

Definition: Der Speicherbedarf eines Algorithmus gibt an, wie viel zusätzlichen Speicher er abhängig von der Eingabegröße n braucht.

Auch der Speicherbedarf wird in O-Notation angegeben. Bubblesort sortiert innerhalb der Liste und braucht nur O(1) zusätzlichen Speicher. Mergesort braucht beim Mischen eine zweite Liste und damit O(n) zusätzlichen Speicher. Oft muss man zwischen schneller Laufzeit und wenig Speicher abwägen.
