# Suchen in Listen

Viele Programme müssen in einer Liste nach einem bestimmten Wert suchen, etwa nach einer Kundennummer oder einem Wort im Wörterbuch. Wie schnell das geht, hängt davon ab, welches Verfahren man wählt und ob die Liste sortiert ist.

## Lineare Suche

Definition: Die lineare Suche vergleicht den gesuchten Wert der Reihe nach mit jedem Element der Liste, vom ersten bis zum letzten. Sie endet, sobald sie den Wert gefunden hat oder die Liste zu Ende ist.

Die lineare Suche funktioniert mit jeder Liste, auch mit einer unsortierten. Dafür ist sie langsam: Steht der Wert ganz hinten oder gar nicht in der Liste, braucht sie bei n Elementen genau n Vergleiche. Im Mittel sind es etwa n / 2 Vergleiche.

```python
def lineare_suche(liste, wert):
    for index in range(len(liste)):
        if liste[index] == wert:
            return index
    return -1
```

Die Funktion gibt die Position des Werts zurück oder -1, wenn er nicht vorkommt.

## Binäre Suche

Definition: Die binäre Suche sucht in einer sortierten Liste. Sie vergleicht den gesuchten Wert mit dem mittleren Element und sucht dann nur noch in der Hälfte weiter, in der der Wert liegen muss. Das wiederholt sie, bis sie den Wert gefunden hat oder der Suchbereich leer ist.

Voraussetzung ist, dass die Liste sortiert ist. Dann halbiert jeder Vergleich den Suchbereich. Nach k Vergleichen sind von n Elementen nur noch n / 2^k übrig. Deshalb braucht die binäre Suche höchstens etwa log2(n) + 1 Vergleiche.

Bei einer Liste mit 1000 Elementen sind das höchstens 10 Vergleiche, bei einer Million höchstens 20. Die lineare Suche bräuchte im schlechtesten Fall 1000 oder eine Million Vergleiche.

```python
def binaere_suche(liste, wert):
    links = 0
    rechts = len(liste) - 1
    while links <= rechts:
        mitte = (links + rechts) // 2
        if liste[mitte] == wert:
            return mitte
        if liste[mitte] < wert:
            links = mitte + 1
        else:
            rechts = mitte - 1
    return -1
```

Ein typischer Fehler ist es, die binäre Suche auf eine unsortierte Liste anzuwenden. Sie liefert dann falsche Ergebnisse, weil sie Teile der Liste überspringt, in denen der Wert doch stehen könnte.
