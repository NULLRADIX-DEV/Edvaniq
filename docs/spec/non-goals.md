# Nicht-Ziele

Jeder Kernpunkt des MVP hat hier eine Liste dessen, was er bewusst nicht baut. Die Listen folgen dem MVP-Umfang der Roadmap: Dort steht, was ein Kernpunkt im MVP enthält, und diese Seite hält fest, wo die Grenze verläuft. Kernpunkte sind die Punkte 1 bis 12, 14 bis 16, 19, 23, 24 und 27 bis 30 der ursprünglichen MVP-Liste (MVP1 bis MVP30) und die zwölf Ergänzungen, die die Roadmap dem Kern hinzufügt. Wo ein Name der MVP-Liste vom [Glossar](glossary.md) abweicht (Skill-Level-Test, Karteikarten, Smart Review), steht hier der Begriff aus dem Glossar.

Pauschal gilt: Was weder zum MVP-Kern noch zu einem Plus-Paket gehört, wird im MVP nicht gebaut, auch wenn es in der ersten Featureliste stand. Die Listen nennen deshalb nur Teilumfänge, bei denen sich sonst leicht jemand fragt, ob sie dazugehören. Die Meilensteine M0 bis M7 stehen auf GitHub, M6 schließt den MVP-Kern ab und M7 ist das MVP-Plus. Der Satz unter jeder Überschrift nennt, was der MVP enthält, und in Klammern den Meilenstein, in dem es entsteht.

Die Spalte „Wohin“ sagt, was aus dem Ausgeschlossenen wird:

| Wohin | Bedeutung |
|---|---|
| Nach dem MVP | Die Roadmap führt es in ihrer Liste „Nach dem MVP“. Es ist dokumentiert, hat aber noch kein Issue. |
| MVP-Plus (P1 bis P6) | Es gehört zu einem optionalen Paket aus M7. |
| Nicht geplant | Es steht in keiner Liste. Kommt es später, wird es zuerst in die Liste „Nach dem MVP“ aufgenommen. |
| Nie | Es ist ausgeschlossen und wird auch nach dem MVP nicht gebaut. |

Das MVP-Plus besteht aus sechs Paketen, die in dieser Reihenfolge entstehen: P1 Gamification (XP, Level, Streak), P2 adaptive Schwierigkeit und „Erklär es anders“, P4 Prüfungsmodus, P5 KI-Tutor, P3 Fotos und Scans, P6 Android-Test-APK. Sie beginnen erst, wenn der Pilot stabil läuft.

## Kernpunkte der MVP-Liste

Die Reihenfolge folgt der MVP-Liste. Punkte, die nur im MVP-Plus entstehen, stehen am Ende der Seite.

### MVP1 Konto und Anmeldung

Der MVP enthält die Registrierung mit E-Mail, Passwort und Einladungscode, Bestätigungsmail, Login, Passwort-Reset, die Altersangabe mit versionierter Zustimmung und das Löschen des Kontos durch den Nutzer (M2, M5).

| Nicht im MVP | Wohin |
|---|---|
| Social Login und Schul-SSO | Nach dem MVP |
| Zwei-Faktor-Anmeldung | Nach dem MVP |
| „Auf allen Geräten abmelden“ | Nach dem MVP |
| Registrierung ohne Einladungscode | Nach dem MVP |
| Lernpräferenzen im Konto | Nach dem MVP |
| Option „Angemeldet bleiben“ | Nicht geplant |
| Ändern von E-Mail-Adresse und Pseudonym | Nicht geplant |
| Oberfläche zur Verwaltung der Einladungscodes | Nicht geplant |
| Wiederherstellen eines gelöschten Kontos | Nicht geplant |
| Login in der Android-App | MVP-Plus (P6) |

### MVP2 Lernziel anlegen

Ein Assistent legt das Lernziel mit Name, Art, Prüfungsart als Label, optionalem Prüfungstermin, Stufe, Ausgabesprache und optionalen Themen an (M2).

| Nicht im MVP | Wohin |
|---|---|
| Mehrere Prüfungsteile mit eigenem Termin und eigener Gewichtung, ein Lernziel hat höchstens eine Prüfung | Nach dem MVP |
| Zielniveau (bestehen, gute Note, Bestnote) und Intensität | Nach dem MVP |
| Prüfungsvorlagen realer Prüfungen mit Themengewichtung und Dauer | Nach dem MVP |
| Lernpfade mit Meilensteinen aus dem Freitext-Ziel, das Freitext-Ziel bleibt ein Label | Nach dem MVP |
| Prüfungsteile, die sich nicht abfragen lassen, etwa Praxis oder Projekt | Nicht geplant |
| Weitere Arten des Lernziels wie „Zertifizierung bestehen“, es bleiben Prüfungsvorbereitung und Freies Lernen | Nicht geplant |

### MVP3 Art des Lernziels

Der Assistent bietet Prüfungsvorbereitung und Freies Lernen an. Freies Lernen mit eigenem Material läuft wie die Prüfungsvorbereitung, ohne Material erzeugt die KI ein Themengerüst, das der Nutzer bestätigt und ändert (M2, M5).

| Nicht im MVP | Wohin |
|---|---|
| Lernpfade mit Meilensteinen und Vorlagenpfade wie „C# Developer“ | Nach dem MVP |
| Projektbasiertes Lernen | Nach dem MVP |
| Themenempfehlungen, Explore und „Teach me something“ | Nach dem MVP |
| Steuern der Tiefe des Themengerüsts | Nicht geplant |
| Themengerüst mit mehr als etwa 20 Themen | Nicht geplant |

### MVP4 Material hochladen

Hochladen lassen sich PDF mit Textlayer, DOCX, TXT, Markdown und eingefügter Text mit festen Grenzen (20 MB und 100 Seiten je Datei, 10 Dokumente je Lernziel). Jedes Dokument zeigt seinen Status, und das Material lässt sich nachladen und löschen. Gescannte PDFs ohne Text erkennt der MVP und lehnt sie mit Hinweis ab (M2, M5).

| Nicht im MVP | Wohin |
|---|---|
| Fotos von Arbeitsblättern und Hefteinträgen, Verarbeitung gescannter PDFs | MVP-Plus (P3) |
| PowerPoint | Nach dem MVP |
| Webseiten und Links, YouTube über Transkript oder Schnittstelle, eigene Audioaufnahmen | Nach dem MVP |
| Dokumente in der App lesen und Textstellen markieren | Nach dem MVP |
| Notizen mit KI-Überarbeitung | Nach dem MVP |
| Material an andere Nutzer weitergeben | Nach dem MVP |
| Tabellen, Formeln und Bilder aus Dokumenten | Nicht geplant |
| Erkennen ähnlicher Dokumente, erkannt werden nur identische Dateien je Nutzer | Nicht geplant |
| Wiederherstellen gelöschter Dokumente | Nicht geplant |
| Größere Dateien und mehr Dokumente als die festen Grenzen | Nicht geplant |

### MVP5 Inhaltsanalyse

Der Content-Worker zerlegt jedes Dokument in Abschnitte mit Dokument und Seite, erkennt Themen, Unterthemen und Kernbegriffe mit Definition und ordnet jeden Abschnitt einem Thema zu. Der Nutzer prüft und ändert die Themen (M2).

| Nicht im MVP | Wohin |
|---|---|
| Erkennen von Formeln, Beispielen, Schwierigkeit und Prüfungsrelevanz je Thema | Nach dem MVP |
| Abhängigkeiten zwischen Themen | Nach dem MVP |
| Zusammenführen und Verschieben von Themen, auch ähnlicher Themen | Nach dem MVP |
| Zuordnung der Abschnitte über Embeddings und Vektorsuche | Nach dem MVP |
| Globale Suche über Material, Zusammenfassungen, Karten und Lernziele | Nach dem MVP |
| Mehr als zwei Ebenen von Themen | Nicht geplant |
| Glossar-Ansicht und Karten aus Kernbegriffen | Nicht geplant |
| Eine neue Analyse, die Änderungen des Nutzers überschreibt | Nie |

### MVP6 Zusammenfassungen

Zu jedem Thema gibt es eine Zusammenfassung in den Längenstufen Kurz, Standard und Detailliert, die erst beim ersten Abruf entsteht und Dokument und Seite als Quelle nennt (M3).

| Nicht im MVP | Wohin |
|---|---|
| „Erklär es mir anders“ in den Stilen einfacher, mit Beispiel und Schritt für Schritt | MVP-Plus (P2) |
| Zusammenfassung nur mit prüfungsrelevanten Inhalten | Nach dem MVP |
| Automatische Erklärhilfe, wenn ein Thema nicht verstanden wird | Nach dem MVP |
| Micro-Learning-Module von 2, 5 und 10 Minuten | Nach dem MVP |
| Export als Markdown oder PDF | Nach dem MVP |
| Zusammenfassung auf Wunsch neu erzeugen | Nicht geplant |
| Weitere Längenstufen oder Darstellungsarten | Nicht geplant |

### MVP7 Einstufung

Der Nutzer bewertet jedes Thema selbst von 1 bis 5, danach kann er einen Kurztest mit ein bis zwei festen Fragen je Thema der oberen Ebene machen. Beides setzt nur den Startwert der Mastery (M5).

| Nicht im MVP | Wohin |
|---|---|
| Adaptiver Test mit Abbruchkriterium, der nach wenigen Fragen die Mastery je Thema schätzt | Nach dem MVP |
| Vorwissen aus anderen Lernzielen übernehmen | Nach dem MVP |
| XP und Rückmeldung nach einzelnen Fragen im Kurztest | Nicht geplant |

### MVP8 Lernplan

Ein regelbasierter Planer ohne KI verteilt die Themen nach Umfang und Mastery-Lücke auf die Tage bis zum Prüfungstermin, ohne Prüfungstermin plant er rollierend für sieben Tage. Das Tagesbudget teilt er nach Prüfungsnähe auf die aktiven Lernziele auf, der Plan erscheint als Liste (M3, M5).

| Nicht im MVP | Wohin |
|---|---|
| Kalenderansicht als Woche und Monat | Nach dem MVP |
| Kalenderintegration per ICS-Abo | Nach dem MVP |
| Plan manuell anpassen, also Blöcke verschieben, Themen überspringen oder fixieren | Nach dem MVP |
| Pausen- und Urlaubsmodus | Nach dem MVP |
| Last-Minute-Modus „Prüfung morgen“ | Nach dem MVP |
| Reihenfolge der Themen nach Abhängigkeiten oder Lernpfad | Nach dem MVP |
| Planung durch KI | Nicht geplant |
| Minutengenaue Planung | Nicht geplant |
| Unterschiedliche Tagesbudgets je Wochentag in der Oberfläche, das Modell kennt sie, die Oberfläche setzt einen Wert für alle Tage | Nicht geplant |
| Manuelles Gewichten der Lernziele und Vorschläge zur Terminverschiebung | Nicht geplant |

### MVP9 Tägliche Sessions

Home zeigt den Tagesplan mit Blöcken, fälligen Karten, Prüfungs-Countdown und Fortschrittsring. Eine Session führt durch Wiederholung, Lerneinheit „Neues Thema“, Quiz und Abschluss mit ermutigender Rückmeldung (M3, M4).

| Nicht im MVP | Wohin |
|---|---|
| Session mit frei gewählter Dauer von 5, 10 oder 15 Minuten | Nach dem MVP |
| Energieabfrage vor der Session | Nach dem MVP |
| Anpassen der Session bei Ermüdung oder Überforderung | Nach dem MVP |
| Fokusmodus mit Timer und Pomodoro | Nach dem MVP |
| Lernen und Fortsetzen ohne Netz | Nach dem MVP |
| Konzentration, Energie oder Frust aus Stimme, Gesicht oder Tippdynamik ableiten | Nie |
| XP und Level im Abschluss | MVP-Plus (P1) |
| KI-Tutor mit dem Chat „Frag zu diesem Thema“ | MVP-Plus (P5) |
| „Erklär es mir anders“ in der Lerneinheit „Neues Thema“ | MVP-Plus (P2) |

### MVP10 Karten

Karten mit Frage und Lösung entstehen per KI je Thema oder von Hand, der Nutzer deckt sie auf und bewertet sich mit Nochmal, Schwer, Gut oder Leicht. Aus einer falsch beantworteten Frage lässt sich eine Karte speichern (M4).

| Nicht im MVP | Wohin |
|---|---|
| Weitere Kartentypen wie Bild, Lückentext, Formel, Beispiel und Wahr/Falsch | Nach dem MVP |
| Import aus CSV, Quizlet und Anki | Nach dem MVP |
| Karten aus Notizen und markierten Textstellen | Nach dem MVP |
| Export als CSV | Nach dem MVP |
| Lernen ohne Netz | Nach dem MVP |
| Automatisches Deck aus Fehlerkarten | Nicht geplant |
| Rich-Text-Editor und Massenbearbeitung | Nicht geplant |
| Limit neuer Karten pro Tag selbst einstellen | Nicht geplant |

### MVP11 Quiz

Ein Quiz stellt Fragen aus dem Fragenpool als Single Choice, Multiple Choice und Wahr/Falsch, bewertet binär und zeigt nach jeder Antwort Lösung und Erklärung (M3).

| Nicht im MVP | Wohin |
|---|---|
| Lückentext, Zuordnung, Reihenfolge, Rechenaufgaben sowie Code- und Szenariofragen | Nach dem MVP |
| Freitextfragen mit KI-Bewertung | Nach dem MVP |
| Feynman-Modus | Nach dem MVP |
| Vollständige Fehleranalyse mit späterer Kontrollfrage | Nach dem MVP |
| Eigene Fragen anlegen | Nach dem MVP |
| Gemeinsames Live-Quiz | Nach dem MVP |
| Boss Challenges als größere Tests zu einem Thema | Nach dem MVP |
| Adaptive Schwierigkeit | MVP-Plus (P2) |
| Prüfungsmodus mit Zeitlimit | MVP-Plus (P4) |
| „Erklär es mir anders“ am Feedback der Antwort | MVP-Plus (P5) |

### MVP12 Quizgenerierung

Je Thema entsteht im Hintergrund ein Fragenpool mit 15 bis 20 Fragen der Schwierigkeit 1 bis 3, geprüft auf Schema, eindeutige Lösung und Duplikate. Bei knappem Bestand wird nachgeneriert (M3).

| Nicht im MVP | Wohin |
|---|---|
| Fragen, die erst beim Quiz passend zur Mastery des Nutzers erzeugt werden | Nicht geplant |
| Fachliche Prüfung jeder Frage durch einen Menschen, stattdessen meldet der Nutzer falsche Fragen | Nicht geplant |
| Fragen eines Nutzers für andere Nutzer wiederverwenden | Nicht geplant |
| Neuerzeugen eines vorhandenen Pools, wenn Themen oder Dokumente dazukommen | Nicht geplant |

### MVP14 Schwächen-Erkennung

Ein Thema gilt als Schwäche bei Mastery unter 50 % oder hoher Fehlerquote zuletzt und als sicher ab 80 %. Schwächen haben Vorrang in Plan und Session, sichere Themen kommen seltener in die Wiederholung (M3, M4).

| Nicht im MVP | Wohin |
|---|---|
| Fehlermuster über viele Antworten | Nach dem MVP |
| Vergessensrate je Thema | Nach dem MVP |
| Weitere Themenzustände oder andere Schwellen, es bleiben vier Zustände | Nicht geplant |

### MVP15 Mastery

Die Mastery von 0 bis 100 % je Nutzer und Thema entsteht aus den Lern-Nachweisen. Neuere und schwerere Antworten zählen stärker, Karten schwächer als Fragen, ohne Wiederholung wirkt ein einfacher Zeitverfall, und die Mastery lässt sich innerhalb der Aufbewahrungsfrist der Lern-Nachweise neu berechnen (M3, M4).

| Nicht im MVP | Wohin |
|---|---|
| Antwortzeit und Confidence in der Formel, sie werden nur mitgespeichert | Nach dem MVP |
| Vergessensmodell auf Themenebene | Nach dem MVP |
| Schwierigkeit je Nutzer aus seinen Antworten lernen | Nach dem MVP |
| Persönliches Lernmodell mit Lern- und Vergessensgeschwindigkeit | Nach dem MVP |
| Verlauf der Mastery über die Zeit | Nach dem MVP |
| Mastery, die über Lernziele hinweg gilt | Nach dem MVP |
| Ein XP-basiertes Level je Thema neben der Mastery | Nie |

### MVP16 Spaced Repetition

Ein etabliertes Verfahren (FSRS oder SM-2) terminiert die Karten, mit Intervallen, die auf die Resttage bis zum Prüfungstermin gedeckelt sind. Neue und fällige Karten begrenzt das Tagesbudget, ein Rückstau wird nach Fälligkeit und Mastery priorisiert (M4).

| Nicht im MVP | Wohin |
|---|---|
| Eigenes Vergessensmodell | Nach dem MVP |
| Auf den Nutzer abgestimmte Parameter | Nach dem MVP |
| Langzeitmodus, der das Wissen nach der Prüfung frisch hält | Nach dem MVP |
| Fragen des Quiz terminieren, terminiert werden nur Karten | Nicht geplant |

### MVP19 Anpassung des Lernplans

Der Planer rechnet den Restplan nach jeder Session, nächtlich und bei geänderten Vorgaben oder neuem Material neu. Heute und morgen bleiben fix, nach verpassten Tagen fragt die App „Plan neu berechnen?“, und jeder Block nennt seinen Grund (M3, M5).

| Nicht im MVP | Wohin |
|---|---|
| Blöcke manuell verschieben | Nach dem MVP |
| Pausen- und Urlaubsmodus | Nach dem MVP |
| Anpassen an Lerntempo, Ermüdung oder eine schlechte Session | Nach dem MVP |
| Eigener Worker-Prozess für die nächtliche Neuplanung, sie läuft als Hintergrunddienst in Planning | Nicht geplant |

### MVP23 Tagesziele

Das Tagesziel ist erreicht, wenn alle für heute geplanten Blöcke erledigt sind. Home zeigt dazu die Minuten aus dem Tagesbudget und einen Fortschrittsring (M3).

| Nicht im MVP | Wohin |
|---|---|
| Frei wählbare Tagesziele mit Messgröße wie Fragen, Karten oder XP | Nach dem MVP |
| Wochenziele | Nach dem MVP |
| Quests und Challenges | Nach dem MVP |
| XP für ein erreichtes Tagesziel | MVP-Plus (P1) |

### MVP24 Statistiken

Home zeigt aktive Lernzeit heute und diese Woche, beantwortete Fragen, Trefferquote sowie die drei stärksten und die drei schwächsten Themen. Das Gateway führt die Zahlen aus Planning und LearningEngine zusammen (M5).

| Nicht im MVP | Wohin |
|---|---|
| Verlaufsgraphen für Mastery, Lernzeit und Quiz-Ergebnisse | Nach dem MVP |
| Wochenrückblick | Nach dem MVP |
| Auswertung nach Uhrzeit und Länge der Sessions | Nach dem MVP |
| Produktkennzahlen für den Betreiber | Nach dem MVP |
| Prüfungsbereitschaft in Prozent mit Themenampel | Nach dem MVP |
| XP- und Streak-Kacheln | MVP-Plus (P1) |
| Notenprognose für Minderjährige | Nie |

### MVP27 Wissensbaum

Eine aufklappbare Themenliste mit zwei Ebenen zeigt je Thema Mastery-Balken und Themenzustand und führt zu Zusammenfassung, Karten und Quiz. Zugleich ist sie der Editor, in dem der Nutzer Themen umbenennt, ausblendet und hinzufügt (M2, M4).

| Nicht im MVP | Wohin |
|---|---|
| Grafische Baum- oder Graphansicht | Nach dem MVP |
| Gamifizierte Baumansicht mit rein visueller Freischaltung | Nach dem MVP |
| Themen verschieben oder zusammenführen | Nach dem MVP |
| Abhängigkeiten zwischen Themen | Nach dem MVP |
| Wissensbibliothek und Wissensprofil über Lernziele hinweg | Nach dem MVP |

### MVP28 Wiederholung

Die Wiederholung ist der erste Schritt jeder Session und bündelt fällige Karten, zuletzt falsch beantwortete Fragen und Themen, deren Mastery durch Zeitverfall unter die Schwelle gefallen ist (M4).

| Nicht im MVP | Wohin |
|---|---|
| Wiederholungs-Presets für ein ganzes Thema, als Morning Review oder Evening Review | Nach dem MVP |
| Genauere Priorisierung über ein Vergessensmodell | Nach dem MVP |
| Eigener Bildschirm für die Wiederholung | Nicht geplant |
| Eigene Fälligkeitslogik im Client, fällig sind nur Karten und der Client fragt sie ab | Nicht geplant |

### MVP29 „Jetzt lernen“-Button

Der Hauptbutton auf Home startet den nächsten offenen Block des Tagesplans, nach erledigtem Plan eine Bonus-Wiederholung und ohne Lernziel den Assistenten (M3, M4).

| Nicht im MVP | Wohin |
|---|---|
| Zeitwahl von 5, 10 oder 15 Minuten vor dem Start | Nach dem MVP |
| Eigener Einstieg „Prüfung morgen“ | Nach dem MVP |
| Anpassen an Energie oder Lerntempo | Nach dem MVP |

### MVP30 Synchronisation zwischen Geräten

Der Server ist die einzige Datenquelle. Die responsive Web-App für Smartphone, Tablet und Desktop lädt den Stand beim Öffnen und bei Fokus neu und zeigt bei Netzverlust Hinweis und Retry (M1).

| Nicht im MVP | Wohin |
|---|---|
| Offline-Modus mit späterem Abgleich | Nach dem MVP |
| Veröffentlichung in den Stores und iOS-App | Nach dem MVP |
| Hell-, Dunkel- und OLED-Themes, Hochkontrast, einstellbare Schriftgröße, dyslexiefreundliche Darstellung | Nach dem MVP |
| Android-App als Test-APK | MVP-Plus (P6) |
| Echtzeit-Abgleich zwischen Geräten | Nicht geplant |

## Ergänzungen

Die Ergänzungen stehen in der Reihenfolge der Roadmap. Was schon bei einem Punkt der MVP-Liste steht, nennen sie nicht noch einmal.

### Plattform-Fundament

Der MVP enthält eine Vorlage für jeden Service, einen Durchstich durch alle Services (Walking Skeleton), Integration Events über [NATS JetStream](../decisions/broker.md) mit Outbox und Inbox und das gemeinsame Ausliefern aller Prozesse aus einem Commit (M0, M1).

| Nicht im MVP | Wohin |
|---|---|
| Vektor-Datenbank | Nach dem MVP |
| Sagas und Event Sourcing | Nicht geplant |
| Mehr als die neun Ereignistypen des Katalogs | Nicht geplant |
| Weitere Broker-Instanzen oder Clustering | Nicht geplant |
| Zurücksetzen von Migrationen bei einem Rollback, Migrationen sind abwärtsverträglich | Nicht geplant |
| Eigene Worker-Prozesse außer bei Content und Notifications, andere Services nutzen Hintergrunddienste im Prozess | Nicht geplant |
| Ein neuer Service ohne die Vorlage | Nie |

### Betrieb auf dem VPS

Produktiv laufen die sieben Services Identity, Planning, Content mit Worker, Knowledge, Assessment, Flashcards und LearningEngine sowie Gateway und Web, jeder Prozess mit Speicherlimit. Dazu gehören Backup mit Restore-Test, ein externer Uptime-Check, Alarme und Log-Rotation (M0, M1, M5).

| Nicht im MVP | Wohin |
|---|---|
| Analytics und Notifications im Betrieb, beide bleiben Skelette ohne Deployment und ohne Datenbank | Nach dem MVP |
| Gamification im Betrieb | MVP-Plus (P1) |
| Tutor im Betrieb | MVP-Plus (P5) |
| Zentrales Log- und Monitoring-System | Nicht geplant |
| Dashboard für Nutzer mit Fehlerquoten | Nicht geplant |
| Updates, die ohne Review eingespielt werden | Nicht geplant |
| Größere Versionssprünge von Frameworks während des Piloten | Nicht geplant |

### Sicherheits-Basis

Der MVP enthält den Test „Nutzer A sieht B nicht“ in jedem Service, einen eigenen Datenbank-Benutzer je Service, eine Begrenzung der Anfragen pro Konto am Gateway, nicht öffentlich erreichbare Verwaltungswerkzeuge und die Darstellung aller Inhalte als bereinigtes Markdown unter Content-Security-Policy (M0, M2, M3, M4).

| Nicht im MVP | Wohin |
|---|---|
| Admin-Oberfläche für Nutzerverwaltung, gemeldete Inhalte und KI-Verbrauch | Nach dem MVP |
| Virenscan, bevor Uploads an andere Nutzer gehen | Nach dem MVP |
| Nutzerspezifische Tarife und Limits | Nicht geplant |
| Nutzerspezifisches Freischalten von Funktionen | Nicht geplant |
| Formeleingabe, Formeln werden nur angezeigt | Nicht geplant |
| Code aus Inhalten ausführen oder bearbeiten, Code wird nur angezeigt | Nie |

### KI-Plattform und Kostenkontrolle

Der MVP enthält einen gemeinsamen KI-Baustein mit validierten Ausgaben, Retry, versionierten Prompts, Moderation und Kostenlogging, einen Anbieter mit EU-Verarbeitung und Auftragsverarbeitungsvertrag, Tageslimits je Nutzer, ein globales Monatslimit mit Not-Aus und einen simulierten Anbieter für Tests (M1, M2).

| Nicht im MVP | Wohin |
|---|---|
| Abrechnen der KI-Kosten gegenüber Nutzern | Nach dem MVP |
| Prompts für Tutor und Erklärvarianten, Bildmoderation, Schutzschicht des Tutors | MVP-Plus (P2, P3, P5) |
| Zweiter Anbieter als Ausweichlösung mit automatischem Wechsel | Nicht geplant |
| Mehrere Anbieter parallel im Betrieb | Nicht geplant |
| Texterkennung auf dem eigenen Server | Nicht geplant |
| Generierte Inhalte eines Nutzers für andere Nutzer wiederverwenden | Nicht geplant |
| Umfangreiche Benchmark-Sammlung, das Referenzset hat fünf bis acht Dokumente | Nicht geplant |
| Ein Anbieter ohne EU-Verarbeitung, Auftragsverarbeitungsvertrag oder Trainingsverzicht | Nie |
| Personenbezogene Daten in Prompts | Nie |

### Recht, Datenschutz und Jugendschutz

Der MVP enthält die Festlegung von Betreiber und Verantwortlichem, ein Verarbeitungsverzeichnis, eine DSFA-Kurzfassung, Auftragsverarbeitungsverträge, versionierte Rechtstexte mit kindgerechter Kurzfassung, die Löschkaskade und einen dokumentierten Prozess für Auskunft und Export. Im Pilot registrieren sich Volljährige und Minderjährige mit schriftlicher Einwilligung der Eltern (M0, M2, M4, M5).

| Nicht im MVP | Wohin |
|---|---|
| Digitaler Eltern-Einwilligungsflow, Pflicht vor dem öffentlichen Start mit Schülern | Nach dem MVP |
| Öffentlicher Start und öffentliche Registrierung | Nach dem MVP |
| Markenrecherche für „Edvaniq“ bei DPMA und EUIPO | Nach dem MVP |
| Self-Service-Export aller eigenen Daten, im MVP beantwortet der Betreiber Anfragen über den dokumentierten Prozess | Nach dem MVP |
| Geschäftsmodell, Käufe und Bezahlfunktionen, der MVP bleibt kostenlos | Nach dem MVP |
| Freunde und Lerngruppen | Nach dem MVP |
| Lehrer- und Klassenmodus, erst nach AVV-Modell, schulrechtlicher Freigabe und Prüfung nach der KI-Verordnung | Nach dem MVP |
| Self-Service für Eltern beim Löschen | Nicht geplant |
| Automatisches Erkennen von Personendaten in Dokumenten und Freitext, der Nutzer bestätigt stattdessen beim Hochladen, nur eigenes Material zu verwenden | Nicht geplant |
| Tracking, das eine Einwilligung braucht, Klar- und Schulnamen, öffentliche Profile | Nicht geplant |
| Abfragen von Gesundheitsdaten | Nie |

### Qualitätssicherung der KI-Inhalte

Der MVP enthält für jede Frage, Karte und Zusammenfassung den Abschnitt sowie Modell- und Promptversion, die Anzeige von Dokument und Seite, das Label „KI-generiert“ mit dem Hinweis „KI kann Fehler machen“ und einen Button „falsch / unangemessen melden“, der das Item sofort pausiert und dem Team die Meldung per Liste oder E-Mail schickt (M3, M4).

| Nicht im MVP | Wohin |
|---|---|
| Feedback „hilfreich“, „zu schwer“ oder „zu leicht“ mit Rückwirkung auf die Generierung | Nach dem MVP |
| Vollständiges Moderations-Backend für gemeldete Items | Nach dem MVP |
| Notifications-Service für Meldungen | Nach dem MVP |
| Original öffnen bei gescannten Dokumenten | MVP-Plus (P3) |
| Öffentliches Meldeformular | Nicht geplant |

### Lern-Nachweis-Vertrag, Themenmodell und Themenzustände

Der MVP enthält einen versionierten Vertrag für den Lern-Nachweis mit Aufbewahrungsfrist, einheitliche Zeitregeln (UTC, Lerntag ab 04:00 Uhr Europe/Berlin), Themen mit stabilen IDs und vier Themenzustände mit zentralen Schwellen (M1, M3). Was Mastery, Zustände und Themen nicht können, steht bei MVP14, MVP15 und MVP27.

| Nicht im MVP | Wohin |
|---|---|
| Themen, die zu mehreren Lernzielen gehören, das Modell ist vorbereitet und im MVP gehört ein Thema genau einem Lernziel | Nach dem MVP |
| Streak auf Basis des Lerntags | MVP-Plus (P1) |
| Ein Service, der den Themenzustand selbst nachrechnet, die anderen übernehmen ihn per Ereignis | Nie |

### Lebenszyklus von Lernziel und Session

Der MVP enthält das Bearbeiten, Löschen und Abschließen eines Lernziels nach dem Prüfungstermin sowie höchstens eine aktive Session je Nutzer, deren Abbruch als Teilwertung zählt (M3, M5).

| Nicht im MVP | Wohin |
|---|---|
| Archiv und Historie abgeschlossener Lernziele | Nach dem MVP |
| Wechsel der Art des Lernziels nach dem Anlegen | Nicht geplant |
| Papierkorb und Wiederherstellen gelöschter Lernziele | Nicht geplant |
| Eine Session fortsetzen, nachdem ihr Lernziel gelöscht wurde | Nicht geplant |
| Mehr als eine aktive Session je Nutzer | Nicht geplant |

### Session-Zusammenstellung

Der MVP enthält eine einzige Prioritätsregel, die Tagesblöcke, Wiederholung und „Jetzt lernen“ füllt: fällige Wiederholungen vor Schwächen vor dem nächsten neuen Thema, bis das Tagesbudget voll ist (M4). Weitere Ausschlüsse stehen bei MVP9 und MVP29.

| Nicht im MVP | Wohin |
|---|---|
| Next Best Action mit Nutzen-Score und „Warum das jetzt?“, sie ersetzt später die Prioritätsregel | Nach dem MVP |
| Daten anderer Services in Planning zwischenspeichern, Planning fragt Mastery und fällige Karten ab | Nie |

### Onboarding, Leerzustände und Demo-Lernziel

Der MVP enthält den Assistenten bis zur ersten Session mit Statusanzeige und Selbsteinschätzung während der Analyse, Leerzustände mit dem nächsten Schritt in jeder Kernansicht, ein Demo-Lernziel aus frei lizenziertem Material per Klick und die öffentliche Startseite mit Kurz-FAQ und Feedback-Adresse (M3, M4, M5).

| Nicht im MVP | Wohin |
|---|---|
| Onboarding mit Testpersonen unter 18 | Nach dem MVP |
| Weitere Demo-Fächer | Nicht geplant |
| Marketing-Inhalte und Blog auf der Startseite | Nicht geplant |
| Video-Tutorials | Nicht geplant |
| Selbstbedienungs-Support in der App | Nicht geplant |

### Spezifikation und Abnahme

Der MVP enthält Glossar, Objektmodell mit Owner je Objekt, diese Nicht-Ziele, zwei bis drei Akzeptanzkriterien mit Testfall je Kernpunkt, messbare Qualitätsanforderungen und die Modusregeln für Übung, Einstufung und Prüfungsmodus (M0).

| Nicht im MVP | Wohin |
|---|---|
| Datenbankschemata im Objektmodell, sie stehen in den Migrationen | Nicht geplant |
| Nutzerspezifische Einstellungen für Zeit oder Fragenzahl im Prüfungsmodus | Nicht geplant |

### Stabilisierung und geschlossener Pilot

Der MVP enthält einen Feature-Freeze, einen E2E-Smoke-Test des Kernflusses nach jedem Deploy, zwei bis vier Wochen geschlossenen Pilot mit Einladungscode und Feedback-Weg und danach die Entscheidung über Weiterbetrieb oder Abschaltung (M6). Was zum Feature-Freeze nicht fertig ist, wird gestrichen, zuerst die Statistik-Kacheln (MVP24), dann das Themengerüst ohne Material (MVP3), dann der Kurztest der Einstufung (MVP7).

| Nicht im MVP | Wohin |
|---|---|
| Öffentliche Werbung und öffentliche Anmeldung für den Pilot | Nach dem MVP |
| Dauerhaftes Analytics-System für die Auswertung des Piloten | Nach dem MVP |
| Neue Features nach dem Feature-Freeze und nicht kritische Schönheitsfehler im Pilot | Nach dem MVP |
| Umsetzen neuer Wünsche aus dem Pilot-Feedback | Nach dem MVP |
| Plus-Pakete, bevor der Pilot stabil läuft | MVP-Plus |
| Ticketsystem für Nutzer | Nicht geplant |
| Teilnahme Minderjähriger ohne Einwilligung der Eltern | Nie |

## Nur im MVP-Plus

Diese Kernpunkte der MVP-Liste und zwei Ergänzungen entstehen erst im MVP-Plus. Jedes Paket lässt sich einzeln abschalten, und halbfertige Pakete bleiben deaktiviert. Die dritte Spalte nennt, was auch das Paket nicht baut.

| Kernpunkt | Paket | Auch im Plus nicht |
|---|---|---|
| MVP20 XP, MVP21 Level, MVP22 Streak | P1 | Multiplikatoren, Boni, Quests und Achievements sowie Streak-Schutz (nach dem MVP), Ranglisten (nicht geplant), Zufallsbelohnungen, Druck-Texte, Käufe, Verlustdrohungen und Themen-Level (nie) |
| MVP13 Adaptive Schwierigkeit, MVP18 „Erklär es mir anders“ | P2 | Schwierigkeit je Nutzer aus seinen Antworten, weitere Stile und automatische Erklärhilfe (nach dem MVP) |
| Fotos und Scans (Ergänzung) | P3 | Zuschneiden und Bildbearbeitung, Garantie für Handschrift und Formeln und Video (nicht geplant), Webseiten, Links und Audioaufnahmen (nach dem MVP) |
| MVP25 Prüfungsmodus | P4 | Notenschlüssel und Prüfungsvorlagen realer Prüfungen sowie mündliche Prüfung (nach dem MVP), Pausieren einer Prüfung (nicht geplant) |
| MVP17 KI-Tutor | P5 | Globaler Tutor mit Werkzeugen, Vektorsuche über das gesamte Material und Sprachmodus (nach dem MVP), Whitelist erlaubter Themen und eigene Beratungsfunktion (nicht geplant) |
| Android-Test-APK (Ergänzung) | P6 | Store-Veröffentlichung, iOS-App, Offline-Modus, Push-Erinnerungen über den Server, Social Login und Zwei-Faktor-Anmeldung (nach dem MVP) |

MVP26 Push-Erinnerungen gehört zu keinem Paket und kommt nach dem MVP. Im Kern zeigt Home stattdessen Prüfungs-Countdown, offene Blöcke und fällige Karten.
