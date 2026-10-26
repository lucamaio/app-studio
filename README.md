# StudySpace

Fondamenta di un'app di studio local-first in C# e .NET MAUI per Windows e Android. Progetto architetturale: [ARCHITECTURE.md](ARCHITECTURE.md).

## Cosa funziona nella fase 7

- Home con card responsive (una, due o tre colonne), empty-state ed elenco paginato.
- Creazione, apertura e modifica di nome, descrizione, icona opzionale, colore, tag e preferito; date di creazione e modifica conservate in UTC.
- Menu contestuale e pulsante ⋯ per modifica, preferito e cancellazione con conferma esplicita.
- Ricerca testuale, filtro per tag e filtro preferiti combinabili. Premere Cerca/Filtra o Invio nella ricerca per applicare i filtri; Azzera ripristina l'elenco.
- Pagina Preferiti dedicata e dashboard del progetto con conteggi di fonti attive, flashcard e tentativi quiz completati.
- Fonti per progetto: aggiunta di PDF/TXT/Markdown/DOCX o testo manuale, elenco paginato, dettagli, rinomina, eliminazione con conferma e stati Elaborazione/Pronto/Errore.
- Copia privata dei file originali, estrazione del testo, anteprima paginata, chunk con provenienza e recupero degli errori tramite rielaborazione della copia locale.
- Progetti salvati in SQLite e disponibili dopo il riavvio, senza account o rete.
- Shell adattiva: menu compatto e pannello laterale su finestre larghe.
- Navigazione verso Progetti, Preferiti, Statistiche e Impostazioni. Le statistiche globali sono ancora un placeholder.
- Tema Sistema predefinito, Chiaro e Scuro; preferenza persistita e aggiornamento automatico del tema di sistema.
- MVVM con CommunityToolkit.Mvvm, DI, validazione, stato di caricamento, errori recuperabili e logging essenziale.

La dashboard apre Fonti, Flashcard, Quiz, **Storico quiz** e **Statistiche progetto**. Il pulsante **Nuovo quiz** apre la configurazione. La voce Statistiche globale nella Shell resta una pagina introduttiva. Non sono presenti credenziali o API key.

## Quiz: configurazione, svolgimento e ripresa

Scegliere **Risposta chiusa**, **Risposta aperta** o **Misto**, 5/10/15/20 domande oppure un numero personalizzato da 1 a 50. Il misto richiede almeno due domande e distribuisce i tipi circa a metà, con un'eventuale chiusa in più. La difficoltà può essere Facile, Media, Difficile o Mista; in quest'ultimo caso si richiedono difficoltà diverse. Selezionare le parti di fonti pronte e attive del progetto con il selettore riutilizzato dalle flashcard: massimo 20 chunk e 40.000 caratteri. `QuizFilters` predispone filtri SourceIds/TopicIds/TagIds: le fonti sono già selezionabili, mentre argomenti e tag non sono ancora esposti e una richiesta non supportata viene rifiutata esplicitamente.

`IAiStudyService` espone generazione quiz, flashcard e valutazione aperta. La generazione rimane una **demo locale** di completamento dai brani; se mancano brani distinti, ridurre il numero o aggiungere materiale. La valutazione aperta usa adesso un **modello reale tramite Ollama**: il vecchio confronto di parole chiave è stato rimosso. Senza configurazione o in caso di errore, la risposta resta salvata e da valutare. Nessuna API key o SDK di provider aggiunto.

Durante lo svolgimento la barra numerata resta fuori dallo scorrimento del contenuto. Ogni numero è cliccabile; su telefono la barra scorre orizzontalmente e porta in vista la domanda corrente. Simboli e descrizioni accessibili distinguono non risposta (○), risposta non verificata (●), corretta (✓), errata (✕) e parziale (◐); il bordo evidenzia separatamente la corrente. Sono presenti “Domanda N di M”, progress bar delle risposte salvate e precedente/successiva.

- **Chiusa:** la scelta salva e corregge nella stessa transazione, mostra soluzione, spiegazione e citazioni e blocca le opzioni. Nessuna chiamata al provider per la correzione.
- **Aperta:** il testo viene salvato dopo circa 650 ms di inattività e prima di cambio domanda, verifica e uscita. Scrivere non avvia la valutazione. **Verifica ora** salva prima il testo, chiama il servizio e blocca una risposta valutata; un errore mantiene testo e possibilità di riprovare senza assegnare zero.
- **Termina quiz:** richiede conferma, rende il tentativo non modificabile e valuta soltanto aperte risposte e non già verificate. Le non risposte non chiamano il provider e valgono zero. Ogni domanda vale un punto; per le aperte il servizio applica i pesi della rubrica, salvata prima della risposta. Corrette ≥0,8, parziali >0 e <0,8, errate =0. Il riepilogo distingue le non risposte. I nuovi feedback semantici conservano lo scoring versione 2; i feedback storici della fase 5 restano immutati nella versione 1. Il tentativo completato registra la massima versione presente nei suoi feedback.
- **Errori di valutazione finale:** lo stato resta `SubmittedPendingEvaluation`, il punteggio finale è assente e **Riprova valutazioni** riprende solo il lavoro mancante. Il completamento è idempotente e apre il risultato completo.
- **Salva e esci / indietro:** salva e lascia il tentativo `InProgress`. Da Quiz/Storico riaprire il tentativo per riprendere domanda e risposte. **Abbandona tentativo** richiede conferma e imposta `Abandoned`; `Completed` e `Abandoned` sono consultabili in sola lettura. Le soluzioni delle non risposte diventano visibili a completamento avvenuto.

All'avvio vengono salvati snapshot immutabili di domande, ordine delle opzioni, soluzioni, rubriche e citazioni. Archiviare una fonte o cambiare il quiz corrente non riscrive il tentativo. Prima della verifica la UI non mostra citazioni o soluzione; dopo la verifica sono disponibili nel feedback. Le valutazioni sono accettate soltanto se coincidono domanda, revisione della risposta e token dell'operazione; una verifica interrotta diventa riprovabile alla riapertura. Il tempo attivo ha checkpoint ogni cinque secondi e viene fermato durante valutazione e sospensione. Una terminazione forzata può perdere l'ultimo testo/intervallo non ancora salvato; i salvataggi falliti vengono segnalati e non autorizzano il cambio domanda o l'uscita.

## Risultati, storico e difficoltà

**Risultato quiz** si apre dopo il completamento o da un tentativo concluso. Mostra punti, percentuale (con credito parziale), durata attiva salvata, numero di domande e quattro conteggi disgiunti: corrette, parziali, errate, non risposte. Il dettaglio presenta cinque domande per pagina con domanda, risposta data (testo dell'opzione scelta per le chiuse), soluzione ideale, spiegazione, feedback salvato e fonti cliccabili. Tutte le domande rimangono consultabili; passando pagina lo scorrimento porta al dettaglio. La fonte apre l'estratto storico con titolo e localizzatore, anche se nel frattempo il documento è stato rinominato o archiviato. Nessuna rigenerazione o rivalutazione durante la consultazione.

**Storico quiz**, nella dashboard, elenca tutti i tentativi in pagine da 30: data locale di avvio, tipo, difficoltà anche mista, domande, punti, percentuale e durata attiva. I completati aprono il risultato; gli altri consentono di riprendere o consultare il tentativo nel suo stato. In attesa di valutazione non viene mostrato un punteggio finale fittizio.

**Statistiche progetto** mostra quiz completati, media aritmetica delle percentuali dei singoli quiz e ultimi dieci quiz completati tramite barre semplici, con collegamento ai risultati. Tentativi in corso, abbandonati o in attesa non entrano nelle statistiche. Le categorie più difficili e quelle in miglioramento sono proiezioni locali di `StudyPerformanceService`, completamente separato dall'AI.

La sezione **Argomenti da migliorare** usa quattro dimensioni esplicitamente etichettate:

- Argomenti: titoli/sezioni dei chunk disponibili al momento della generazione.
- Tag: etichette delle fonti citate e tag del progetto, copiati nel tentativo; i tag del progetto sono contesto comune a tutte le sue domande.
- Fonti: ID e titoli storici delle fonti citate.
- Concetti: concetti attesi della rubrica, raggruppati per testo normalizzato, senza dedurre sinonimi con l'AI.

I metadati vengono congelati nei nuovi snapshot. I vecchi tentativi privi di argomenti/tag continuano a contribuire tramite fonti e concetti già salvati: non si applicano retroattivamente etichette attuali. Nelle aperte i gradi dei singoli criteri e i riferimenti ai chunk distribuiscono il peso su fonti/argomenti/tag pertinenti; un errore su un criterio non viene attribuito automaticamente a quelli corretti. Nelle chiuse si distribuisce il punto sulle dimensioni citate. Una domanda conta una sola volta per ciascuna categoria, anche con più criteri o citazioni.

Indice storico di difficoltà = `1 - punti ottenuti / punti possibili` delle sole risposte valutate; da 0,3 viene suggerito il ripasso. Le non risposte sono conteggiate separatamente e non creano da sole una difficoltà concettuale. Ogni scheda indica errori e parziali nelle ultime cinque domande valutate pertinenti, totale delle evidenze e riferimenti/suggerimenti già disponibili. Con meno di tre evidenze si dichiara il limite. Ordinamento: indice e quantità di evidenze; fino a tre voci per dimensione, per mantenere le schermate leggibili.

Per il miglioramento si confrontano fino a cinque domande dell'ultimo quiz pertinente con cinque dei quiz precedenti: almeno tre risposte in ciascun periodo e aumento della copertura pesata di almeno 10 punti percentuali. Un solo quiz non basta per segnalare miglioramento. L'ordine delle evidenze è data di completamento/ID del tentativo, poi posizione delle domande; non pretende di ricostruire l'ordine temporale dei singoli tap. Le percentuali sono indicative: quiz, difficoltà, modelli e scoring diversi richiedono contesto; i quiz con domande dimostrative sono segnalati.

L'analisi scorre lo storico a blocchi di 20 tentativi con cursore stabile su data di completamento e ID, senza caricare tutti i tentativi insieme. Per ciascuna categoria conserva contatori, finestre limitate e fino a tre riferimenti. Il risultato storico usa solo tentativi completati entro la propria data di conclusione; quiz successivi non riscrivono quella lettura delle difficoltà. Le statistiche del progetto includono progressivamente le nuove evidenze. Il calcolo è ricostruibile dai dati salvati e non scrive diagnosi nella tabella `StudyWeakness`, predisposta per una futura tassonomia esplicita di TopicId.

## Valutazione semantica con Ollama

1. Avviare un server Ollama con un modello già installato capace di seguire istruzioni in italiano e produrre output strutturati. StudySpace non installa modelli né avvia il server. La qualità semantica dipende dal modello scelto.
2. In **Impostazioni → Valutazione delle risposte aperte**, attivare Ollama, indicare l'indirizzo del server e il nome esatto del modello, quindi **Salva impostazioni AI**. La funzione è disattivata inizialmente; modificare i campi non effettua richieste.
3. Su Windows il valore iniziale è `http://localhost:11434`. Nell'emulatore Android il computer host è normalmente `http://10.0.2.2:11434`; su un telefono usare l'IP privato del computer o un server HTTPS raggiungibile. Ollama deve ascoltare sull'interfaccia desiderata e il firewall deve consentire la connessione. Non sono cambiati automaticamente firewall o configurazione del server. [Configurazione di rete Ollama](https://docs.ollama.com/faq#how-can-i-expose-ollama-on-my-network).
4. Scrivere la risposta e premere **Verifica ora**. Il salvataggio precede la chiamata; la schermata mostra caricamento e **Interrompi verifica**. **Termina quiz** verifica solo le risposte aperte ancora da valutare. Dopo un errore la chiusura si ferma alla prima chiamata fallita, senza ripetere attese per ogni domanda; **Riprova valutazioni** continua dalle risposte mancanti.

L'adapter invia `POST /api/chat`, `stream:false`, schema JSON in `format` e temperatura zero. Domanda, risposta, rubrica e citazioni storiche costituiscono il contesto: nessun intero progetto viene inviato. Le istruzioni richiedono riconoscimento di parafrasi, sinonimi, negazioni e contraddizioni e vietano di premiare lunghezza, stile o parole presenti. Sono dati non fidati sia la risposta sia il materiale delle fonti; non vengono eseguite istruzioni o strumenti contenuti nel testo. [API e output strutturati Ollama](https://github.com/ollama/ollama/blob/main/docs/api.md).

`OpenAnswerEvaluation` contiene Score 0–100, Status (`Correct`, `PartiallyCorrect`, `Incorrect`), IsCorrect, Summary, CorrectConcepts, MissingConcepts, IncorrectConcepts, Suggestions, IdealAnswer, SourceReferences, Confidence 0–1 e Criteria. Il provider attribuisce gradi semantici ai criteri preesistenti; Application calcola la somma pesata e rifiuta un punteggio dichiarato incoerente (tolleranza 0,05 punti solo per arrotondamenti). Nessuna regola di lunghezza o corrispondenza testuale determina il voto. Il controllo letterale delle citazioni verifica soltanto che l'evidenza esista davvero.

Il parser rifiuta proprietà duplicate, sconosciute o mancanti, JSON/enum malformati, liste nulle, valori fuori intervallo, criteri inventati e citazioni estranee. Schema e prompt aiutano il modello, ma non sostituiscono questa validazione. Il feedback mostra **Corretto**, **Parzialmente corretto** o **Da rivedere**, poi Cosa hai spiegato bene / Cosa manca / Cosa è errato / Come migliorare / Possibile risposta completa e fonti consultabili. La confidenza è dichiarata dal modello, non una probabilità calibrata; sotto 0,6 la UI invita a confrontare le evidenze.

Timeout complessivo di 90 secondi per risposta, annullamento esplicito, errori di connessione, server/modello non disponibile, output troncato e malformato lasciano testo e tentativo recuperabili senza assegnare zero. I risultati tardivi non vengono salvati. I feedback validi sono persistiti con modello, versione del prompt e scoring; riaprire lo storico non ricontatta Ollama. Le valutazioni precedenti non vengono ricalcolate.

Limiti espliciti: risposta studente fino a 8.000 caratteri; estratti fino a 40.000 caratteri; ulteriore budget conservativo di 22.000 byte UTF-8 per payload serializzato e istruzioni, con contesto richiesto di 32.768 token e massimo 8.192 token di output. Il materiale eccessivo viene rifiutato, mai troncato silenziosamente. Il modello deve supportare il contesto richiesto. Corpo HTTP massimo 128 KB, JSON della valutazione massimo 64.000 caratteri, liste e testi ulteriormente limitati. Nessun retry automatico, download, credenziale o richiesta in background. HTTP è ammesso solo su loopback o IP privati IPv4, HTTPS su server configurati; redirect disabilitati. Su Android è abilitato il permesso Internet e il traffico HTTP necessario al server locale: la validazione dell'indirizzo rimane nel servizio. Il testo inviato su HTTP privato non è cifrato, come indicato nelle Impostazioni.

## Flashcard e studio

Dal progetto aprire **Flashcard**: creare e modificare fronte/domanda, retro/risposta, spiegazione facoltativa, difficoltà, etichette e preferita. È possibile collegare una fonte e selezionare brani di riferimento. L'eliminazione richiede conferma; tutte le modifiche sono persistenti in SQLite e verificano la revisione per evitare sovrascritture concorrenti.

La lista è paginata e combina ricerca su domanda/risposta/spiegazione, tag, difficoltà e preferite. Premere **Cerca / Filtra** per applicare i criteri; **Studia risultati** usa l'intero insieme corrispondente ai filtri applicati. La sessione carica gli ID e legge il contenuto di una scheda alla volta. Mostra inizialmente il fronte; **Mostra/Nascondi risposta**, precedente, successiva e mescola consentono il ripasso. Passare a un'altra scheda nasconde la risposta. Non sono ancora presenti spaced repetition, punteggi o storico delle sessioni.

**Dalle fonti · demo** usa `IAiStudyService` con il provider locale `MockAiStudyService`: è una dimostrazione deterministica che prepara completamenti ed estratti letterali, non un modello AI. Non richiede rete o API key. Selezionare fonti pronte e attive e i relativi chunk; limiti: 1–30 schede, 1–20 chunk, massimo 40.000 caratteri di contesto. Il limite è in caratteri, non token. Se il materiale non basta, vengono proposte meno schede con un messaggio esplicito.

L'anteprima consente di selezionare/deselezionare, modificare domanda, risposta, spiegazione, difficoltà e tag, oppure rimuovere proposte. Nessuna scheda viene scritta prima della conferma. I riferimenti ai chunk e le citazioni sono verificati contro il materiale fornito e restano collegati ai brani originali. L'importazione salva in una transazione soltanto le schede selezionate, con provider, provenienza e indicazione di modifica. Un nuovo tentativo della stessa conferma non duplica i dati. Archiviare una fonte conserva le citazioni delle schede esistenti, ma impedisce nuove generazioni da quella fonte.

Per aggiungere un provider reale implementare `IAiStudyService` e sostituire la registrazione in Infrastructure: Application e UI non dipendono da SDK OpenAI. Budget del modello, consenso all'invio dei documenti, configurazione sicura e gestione della rete andranno realizzati insieme a quel provider; nessun invio esterno è implementato ora.

## Fonti: formati, elaborazione e conservazione

| Formato | Estrazione e limiti |
| --- | --- |
| TXT | UTF-8 rigoroso; UTF-8/UTF-16/UTF-32 con BOM. Codifiche non valide e dati binari producono un errore esplicito |
| Markdown (`.md`, `.markdown`) | Testo semplice, titoli come sezioni e etichette dei link; il codice viene conservato. Alcuni marcatori di formattazione possono restare nel testo; HTML non viene eseguito |
| PDF | PdfPig **0.1.16**, testo per pagina. Nessun OCR o inserimento password: scansioni senza testo e documenti illeggibili/protetti mostrano un errore. Colonne, formule e tabelle richiedono controllo dell'anteprima |
| DOCX | `System.IO.Compression` e `System.Xml` di .NET: paragrafi e celle delle tabelle del corpo, titoli riconosciuti dagli stili Heading/Titolo. Nessuna dipendenza da Word. Intestazioni, note, immagini e impaginazione non sono estratte; nessuna pagina viene inventata |
| Manuale | Titolo obbligatorio e testo salvato come revisione; nessun file necessario |

Limiti configurati in `SourceImportOptions`: **25 MB** per file, **2 milioni** di caratteri estratti, **50 MB** di contenuto DOCX decompresso, **2.000 pagine** PDF. I DOCX non vengono decompressi sul filesystem; DTD e risoluzione di entità esterne sono disabilitati. Una sola importazione per volta; copia, parsing e chunking avvengono fuori dal thread grafico, con cancellazione cooperativa. Un parser può completare la pagina corrente prima di osservare l'annullamento.

La lista mostra subito la fonte in elaborazione durante l'importazione di un file. Pronto viene salvato soltanto insieme al testo e a tutti i chunk, in una transazione. Un errore conserva codice e messaggio senza esporre eccezioni grezze; dai dettagli si può riprovare quando esiste una copia locale o un testo manuale. Se la copia non esiste, importare di nuovo il file. Al riavvio le elaborazioni interrotte diventano errori recuperabili. La rielaborazione verifica dimensione e SHA-256 della copia: un file cambiato richiede una nuova importazione.

Il chunker `paragraph-v1` normalizza le terminazioni di riga e conserva i localizzatori. Cerca confini di paragrafo/frase/parola, con obiettivo 3.000 caratteri, massimo 4.000 e sovrapposizione massima 250; un documento breve resta un unico chunk. I chunk conservano ProjectId, SourceId (ricavato dalla revisione nelle query), SourceRevisionId, ordine, offset `[inizio,fine)`, pagine/sezioni e versione. Il limite è in caratteri, non token: un futuro adapter AI dovrà verificare il budget del modello. `ISourceChunkingService` e le query per progetto consentono di aggiungere embedding o ricerca semantica in seguito.

La schermata dettagli visualizza il testo a parti di 6.000 caratteri e i metadati dei chunk a pagine di 20. Eliminare una fonte la **archivia**: scompare dalla lista e dal conteggio attivo, mentre revisione, file e chunk restano disponibili per mantenere valide future citazioni. Eliminare il progetto rimuove i dati SQLite e poi i file del progetto; la pulizia degli orfani all'avvio riprende eventuali cancellazioni interrotte. I file sono confinati alla cartella privata `sources/{projectId}/{sourceId}/{revisionId}/`; non vengono salvati percorsi originali o URI Android permanenti.

PdfPig è distribuito con licenza [Apache-2.0](https://github.com/UglyToad/PdfPig/blob/v0.1.16/LICENSE); i parser DOCX/TXT usano il runtime [.NET con licenza MIT](https://github.com/dotnet/runtime/blob/v10.0.0/LICENSE.TXT). Licenze e avvisi PdfPig sono inclusi come asset in `Resources/Raw/Licenses`. La libreria PDF è confinata in Infrastructure e non aggiunge dipendenze NuGet transitive sui target usati.

## Struttura

| Progetto | Responsabilità |
| --- | --- |
| StudySpace.Domain | Entità senza dipendenze UI, storage o provider |
| StudySpace.Application | Servizi progetti/fonti/flashcard/quiz, tentativi e valutazioni, pipeline di importazione e generazione, contratti indipendenti dai provider |
| StudySpace.Infrastructure | SQLite, mapping, migrazioni, repository, file privati, estrattori PDF/DOCX/TXT/Markdown, chunking, provider dimostrativo e DI |
| StudySpace.App | MAUI, Shell, pagine, ViewModel, tema e adapter di piattaforma |
| StudySpace.UnitTests | Servizio progetti e logica dei ViewModel, senza dispositivo |
| StudySpace.IntegrationTests | Database SQLite reale temporaneo, migrazione e integrità |

I test dei ViewModel compilano gli stessi file sorgente di presentazione attraverso link MSBuild: il livello testato non dipende da MAUI. Nessun progetto di produzione dipende dai test.

## Prerequisiti

Ambiente usato: Windows x64, .NET SDK **10.0.302**, workload set **10.0.303.1**, MAUI **10.0.20**, .NET for Android **36.1.69**. `global.json` fissa SDK e workload set; sono consentite solo patch stabili della stessa feature band SDK.

Dipendenze dirette fissate nei csproj e file `packages.lock.json` versionabili: CommunityToolkit.Mvvm 8.4.2, sqlite-net-pcl 1.11.285, PdfPig 0.1.16, Microsoft.Extensions 10.0.0. Per i test: xUnit 2.9.3 e Microsoft.NET.Test.Sdk 17.14.1.

Installare i workload da un terminale con i privilegi richiesti dall'installer:

```powershell
dotnet workload install maui-windows maui-android
dotnet workload list
```

Occorrono Windows SDK e toolchain Android compatibili con il workload. In questa macchina sono disponibili Android SDK API 36 e Java **21.0.7**, incluso in Android Studio. Il vecchio JDK 17 trovato automaticamente non viene usato. L'installazione workload ha segnalato un riavvio Windows in sospeso: nessun riavvio automatico è stato eseguito.

Per sviluppo con IDE usare un Visual Studio compatibile con .NET 10 e il workload MAUI, oppure VS Code con estensione MAUI. Riferimenti: [installazione ufficiale](https://dotnet.microsoft.com/en-us/learn/maui/first-app-tutorial/install) e [piattaforme supportate](https://learn.microsoft.com/en-us/dotnet/maui/supported-platforms?view=net-maui-10.0).

Minimi scelti per l'app: Android 10/API 29 e Windows 10 1809. Il target Android di compilazione segue il workload; il TFM Windows usa SDK 19041.

## Build della solution

Dalla radice del repository, in PowerShell:

```powershell
# Adattare questi due percorsi alla propria installazione.
$studyJava = 'C:\Program Files\Android\Android Studio\jbr'
$studyAndroid = Join-Path $env:LOCALAPPDATA 'Android\Sdk'

dotnet restore StudySpace.sln --locked-mode
dotnet build StudySpace.sln -c Debug -p:JavaSdkDirectory="$studyJava" -p:AndroidSdkDirectory="$studyAndroid"
dotnet build StudySpace.sln -c Release -p:JavaSdkDirectory="$studyJava" -p:AndroidSdkDirectory="$studyAndroid"
```

Non modificare le variabili di ambiente globali per questo progetto. Per aggiornare intenzionalmente i pacchetti modificare i csproj, eseguire restore senza locked mode e revisionare i lock file. Le directory bin/obj e i database locali sono ignorati da Git.

Durante la verifica della fase 3 l'SDK 10.0.302 di sistema è diventato indisponibile dopo la build Debug. La Release è stata completata con una copia ufficiale dello stesso SDK nella directory ignorata `.build/dotnet`, verificata con SHA-512, riutilizzando i pack di sistema e una copia locale dei manifesti. `global.json` resta invariato. Su questa macchina, finché l'SDK di sistema non viene ripristinato, usare la copia locale da un processo con `DOTNETSDK_WORKLOAD_PACK_ROOTS` impostato alla directory di installazione .NET contenente `packs`:

```powershell
$previousStudyPackRoots = $env:DOTNETSDK_WORKLOAD_PACK_ROOTS
try {
    $env:DOTNETSDK_WORKLOAD_PACK_ROOTS = Join-Path $env:ProgramFiles 'dotnet'
    & .\.build\dotnet\dotnet.exe build StudySpace.sln -c Release -p:JavaSdkDirectory="$studyJava" -p:AndroidSdkDirectory="$studyAndroid"
} finally {
    $env:DOTNETSDK_WORKLOAD_PACK_ROOTS = $previousStudyPackRoots
}
```

La copia locale è una risorsa dell'ambiente di prova e non fa parte dei sorgenti versionabili; su una nuova macchina installare normalmente SDK e workload indicati sopra.

## Windows

```powershell
dotnet build src/StudySpace.App/StudySpace.App.csproj -f net10.0-windows10.0.19041.0 -c Debug
dotnet build src/StudySpace.App/StudySpace.App.csproj -f net10.0-windows10.0.19041.0 -c Debug -t:Run
```

La configurazione corrente è **unpackaged** (`WindowsPackageType=None`), per sviluppo locale. L'eseguibile viene prodotto in `src/StudySpace.App/bin/Debug/net10.0-windows10.0.19041.0/win-x64/`. La distribuzione su altre macchine richiede runtime Windows App SDK/.NET compatibili oppure una futura configurazione di pubblicazione self-contained/MSIX. Firma e Store non sono configurati.

## Android

```powershell
$studyJava = 'C:\Program Files\Android\Android Studio\jbr'
$studyAndroid = Join-Path $env:LOCALAPPDATA 'Android\Sdk'
dotnet build src/StudySpace.App/StudySpace.App.csproj -f net10.0-android -c Debug -p:JavaSdkDirectory="$studyJava" -p:AndroidSdkDirectory="$studyAndroid"

# Con un emulatore avviato o un dispositivo autorizzato tramite adb:
dotnet build src/StudySpace.App/StudySpace.App.csproj -f net10.0-android -c Debug -t:Run -p:JavaSdkDirectory="$studyJava" -p:AndroidSdkDirectory="$studyAndroid"
```

APK di debug: `src/StudySpace.App/bin/Debug/net10.0-android/it.studyspace.app-Signed.apk`. Il deploy Debug predefinito richiede anche gli assembly trasferiti dai tool MAUI (`-t:Run`): per installare il solo APK tramite `adb install`, compilare con `-p:EmbedAssembliesIntoApk=true` oppure usare l'APK Release. La firma usata qui è solo per sviluppo; chiave di rilascio e pubblicazione non sono configurate. Per altri ambienti completare SDK/JDK tramite le istruzioni del workload e accettare le licenze Android necessarie. Un errore XA0033 sul vecchio Java va risolto indicando una toolchain compatibile, non nascondendo l'avviso.

## Test

```powershell
dotnet test tests/StudySpace.UnitTests -c Debug
dotnet test tests/StudySpace.IntegrationTests -c Debug
```

I test SQLite usano cartelle temporanee univoche, eliminate alla fine; non accedono all'archivio personale. Coprono riapertura, migrazioni (incluso aggiornamento da v1), ricerca e filtri combinati, paginazione, tag isolati per progetto, conteggi, conflitti di revisione, rollback e cancellazione dell'intero grafo senza intaccare altri progetti. I test delle fonti aggiungono TXT/BOM, Markdown, PDF multipagina e scansioni, DOCX con tabella/titolo, file corrotti, codifica, limiti, XML con DTD, hash, metadati riaperti, recupero, archivio e pulizia file. Il chunking è verificato per copertura, offset, limiti, sovrapposizione, localizzatori e Unicode. I test di servizio e ViewModel coprono validazione, conferma di eliminazione, preferiti, modifica, navigazione, paginazione del testo/chunk, empty-state, errori, retry dopo un salvataggio già riuscito e tema.

Verifiche precedenti della fase 3 (25 ottobre 2026):

| Verifica | Esito |
| --- | --- |
| Build solution Debug, Windows e Android | Superata, zero errori e zero avvisi |
| Build solution Release, Windows e Android | Superata, zero errori e zero avvisi; trimming e AOT Android completati |
| Test unitari e ViewModel | 29 superati, sia Debug sia Release |
| Test di integrazione SQLite, file e parser | 41 superati, sia Debug sia Release |
| Avvio Windows | Finestra avviata correttamente |
| Controllo interattivo Windows | Card e filtri visualizzati con tre progetti in archivio di prova isolato; dashboard osservata con conteggi attesi. Il controllo non copre ogni interazione |
| UI Android Release | Su emulatore API 35: creazione progetto, dashboard, Fonti, file picker e PDF importato con stato Pronto, dimensione corretta, anteprima leggibile e chunk con pagina/offset corretti. Su API 36.1 verificati anche inserimento testo manuale e relativo chunk; i blocchi di System UI hanno richiesto un emulatore alternativo per la verifica finale |
| Dispositivo Android reale e cambio tema del dispositivo | Da verificare interattivamente; non dichiarati coperti dalla build |

Artefatti Release: eseguibile in `src/StudySpace.App/bin/Release/net10.0-windows10.0.19041.0/win-x64/` e pacchetti in `src/StudySpace.App/bin/Release/net10.0-android/`. Gli artefatti sono locali e non vengono inclusi nel repository.

Verifiche della fase 4 (25 ottobre 2026):

- Solution Debug Windows e Android compilata con zero errori e zero avvisi.
- Solution Release Windows e Android compilata con zero errori e zero avvisi; trimming, AOT Android e creazione del pacchetto completati.
- **103 test superati sia Debug sia Release**: 50 unitari/ViewModel e 53 di integrazione. Le nuove verifiche coprono CRUD, filtri, sessione con risposta nascosta, selezione dei materiali, provider mock, citazioni inventate o fuori progetto, migrazione v3→v4 con dati preesistenti, modifica dell'anteprima, importazione selettiva, rollback e conferma ripetuta anche dopo archiviazione della fonte.
- Prova interattiva Windows Debug con archivio separato: dashboard con conteggi corretti, elenco delle schede con tag e difficoltà, apertura dello studio con solo fronte e visualizzazione del retro tramite Mostra risposta. Le restanti interazioni sono coperte dai test di logica, non da una prova UI completa.
- Il nuovo modulo non è stato provato interattivamente su Android in questa fase; la precedente prova Android Release riguardava le fonti. Resta necessaria la verifica del modulo flashcard su dispositivo, inclusi tastiera, touch, font ingranditi e sospensione.

Verifiche della fase 5 (25 ottobre 2026):

- Solution Debug Windows e Android compilata con zero errori e zero avvisi.
- Solution Release Windows e Android compilata con zero errori e zero avvisi; trimming, AOT e pacchetto Android completati.
- **141 test superati sia Debug sia Release**: 63 unitari/ViewModel e 78 di integrazione SQLite, file e parser. I nuovi casi coprono preset e personalizzato, difficoltà mista, barra navigabile con 50 domande, autosalvataggio senza valutazione, flush e conservazione bozza dopo errore, rubriche e soglie, verifica esplicita, opzioni bloccate, snapshot, citazioni, ripresa, risposte Unicode vuote, valutazioni obsolete, rollback, chiusura ripetuta e cancellazione isolata del grafo.
- Prova Windows Debug con archivio separato: pulsante Nuovo quiz nella dashboard, pagina di configurazione con tipi/numero/difficoltà e selettore fonti, lista di tentativi e apertura del quiz misto con cinque numeri, domanda corrente, avanzamento e fonti nascoste prima della verifica. Il flusso UI completo di risposta/chiusura non è stato dichiarato coperto; i comportamenti sono verificati dai test di servizio e presentazione.
- Nessuna prova interattiva del nuovo quiz su Android in questa fase. Prima della distribuzione verificare barra orizzontale con 50 domande su telefono, tastiera multilinea, verifica/feedback, navigazione indietro con bozza, sospensione/riavvio e caratteri ingranditi su entrambi i target.

Verifiche della fase 6 (25–26 ottobre 2026):

- Solution **Debug e Release Windows/Android** compilata senza errori o avvisi. La verifica finale Release del 26 ottobre è registrata in `artifacts/phase6-release-build.log`; pacchetto Android e build Windows disponibili nei percorsi sopra.
- **204 test superati in Debug e Release**: 79 unitari/ViewModel e 125 di integrazione. Inclusi parsing rigoroso, campi mancanti/duplicati/estranei, punteggi e stati incoerenti, citazioni inventate, errori HTTP/connessione, timeout, annullamento, rifiuto di risultati tardivi, chiusura riprendibile, metadati persistenti e lettura dei feedback precedenti. Esito Release in `artifacts/phase6-release-tests.log`.
- Prova interattiva Windows Debug con archivio isolato: riapertura di una risposta già valutata, blocco della modifica, punteggio/confidenza, tutte le cinque sezioni didattiche, risposta completa e riferimenti. Verificati scorrimento del feedback e barra domande separata su finestra normale e ingrandita. Il feedback usato nella prova è una fixture esplicitamente etichettata, non prodotto da un modello reale.
- Nessun modello Ollama attivo disponibile per una valutazione reale: i test HTTP usano risposte programmate e verificano contratto e affidabilità, non l'accuratezza semantica di un modello. L'attivazione richiede un server e un modello configurati dall'utente; nessun modello è stato scaricato o installato.
- Nessuna prova interattiva Android del modulo di valutazione in questa fase. Prima della distribuzione provare con il modello scelto parafrasi concise corrette, risposte lunghe contraddittorie, omissioni, materiale ambiguo, perdita di connessione e ripresa su Windows e Android.

Verifiche della fase 7 (26 ottobre 2026):

- Solution **Debug e Release Windows/Android** compilata con **zero errori e zero avvisi**. Trimming, AOT e pacchetto Android completati. Log locali: `artifacts/phase7-debug-build.log`, `artifacts/phase7-release-build.log` e corrispondenti `phase7-*-tests.log`.

- **224 test superati sia Debug sia Release**: 94 unitari/ViewModel e 130 di integrazione. Nuovi casi: categorie disgiunte e credito parziale, durata, attribuzione per criterio, quattro dimensioni senza duplicazioni, non risposte escluse dalla diagnosi, miglioramento su quiz distinti e soglia esatta di dieci punti, snapshot di tag/sezioni, fonti rinominate/archiviate, paginazione stabile su oltre venti tentativi, cut-off storico, isolamento, migrazione v5→v6 e lettura dei vecchi JSON. Il ritorno dai riferimenti mantiene la pagina delle domande; il dettaglio raggiunge tutte le cinquanta domande nei test.
- Prova interattiva Windows Debug con archivio separato e tre tentativi di esempio: storico con date/tipo/difficoltà/durata, risultato 3/6 = 50% con 2 corrette, 2 parziali, 1 errata e 1 non risposta; apertura del riferimento storico; statistiche con 3 quiz e media 41,7%, barre recenti e miglioramento di 30 punti; passaggio dal primo gruppo di cinque domande alla sesta con scorrimento automatico sul dettaglio. I feedback di prova erano etichettati e non richiedevano un modello AI.
- La verifica interattiva riguarda Windows. Android è verificato tramite compilazione: restano da provare su telefono/emulatore scorrimento, caratteri ingranditi, navigazione tra risultati/fonti e sospensione. L'apertura automatica dei risultati dopo Termina è coperta dai test dei comandi; la prova UI ha aperto tentativi già conclusi dallo storico.
- README e ARCHITECTURE documentano la provenienza delle categorie e i limiti dell'analisi. Nessun tentativo storico viene rigenerato o rivalutato per costruire statistiche o risultati.

## Dati, configurazione ed errori

- `DatabaseOptions` riceve dalla composition root il percorso assoluto `FileSystem.AppDataDirectory/studyspace.db3`. Nessun percorso personale è codificato nei sorgenti.
- Le sei migrazioni embedded sono applicate in ordine e in transazione. La fase 7 aggiunge soltanto `006_PerformanceIndex.sql` per le letture cronologiche dei quiz completati; le migrazioni 001–005 restano invariate. `005_Quizzes.sql` aggiunge metadati del quiz/tentativo, citazioni storiche, valutazioni per revisione e token/errori di verifica. La migrazione 004 conserva dati preesistenti e popola la ricerca delle vecchie schede. Il database non viene cancellato o ricreato in caso di errore.
- Tutto l'accesso passa dal coordinatore SQLite: una connessione per operazione, foreign key attive, timeout 5 secondi e scritture serializzate fuori dal thread UI. WAL rimane rinviato finché non serve e non è validato.
- GUID memorizzati come testo e date ISO 8601 UTC. Relazioni composite ProjectId/Id impediscono riferimenti tra progetti diversi.
- Nome progetto 1–120 caratteri, descrizione fino a 2000; fino a 10 tag da 32 caratteri, separati da virgole nel modulo. Tag duplicati ignorati senza distinguere maiuscole/minuscole; nomi progetto uguali consentiti. Ricerca senza distinzione di maiuscole e accenti su nome e descrizione, combinabile con il filtro per tag.
- Modifica, preferito ed eliminazione verificano la revisione letta: un conflitto viene segnalato e richiede il ricaricamento. L'eliminazione rimuove in transazione tutti i dati SQLite appartenenti al progetto.
- Il tema usa Preferences, chiave `appearance.theme`; un valore sconosciuto torna a Sistema. Nessun appsettings con segreti.
- Logging tramite `ILogger`: provider Debug nell'output del debugger; su Android anche warning/error nel log di piattaforma con tag `StudySpace`. Nessun archivio di log creato dall'app. Gli errori riportano fase, tipo e codice diagnostico, non nomi dei progetti, contenuti, percorsi personali o parametri SQL.
- Gli errori attesi vengono mostrati nella pagina con possibilità di riprovare. Un errore di caricamento non viene presentato come “nessun progetto”.
- Backup automatico Android disabilitato in questa fase. Sincronizzazione, cifratura integrale e backup/esportazione utente non sono implementati.

## Verifica manuale prima della distribuzione

Su Windows e su Android reale/emulato: partire da un archivio vuoto, creare e modificare progetti con tag, riavviare e controllare la persistenza; combinare ricerca/tag/preferiti, aggiungere e rimuovere preferiti dalle due pagine, aprire dashboard e sezioni. In Fonti importare un documento per formato e un PDF corrotto, inserire testo manuale, rinominare, aprire anteprima e pagine dei chunk, annullare una copia e riprovare dalla copia disponibile. Riavviare e controllare stato/metadati e conteggio dashboard. Verificare annullamento e conferma dell'archiviazione della fonte e della cancellazione del progetto su dati di prova; aprire tutte le voci Shell; passare fra Sistema/Chiaro/Scuro e cambiare il tema del dispositivo con l'app aperta. Provare ridimensionamento, tastiera, caratteri ingranditi, sospensione e ritorno alla home.

Per le flashcard: creare, modificare e riaprire una scheda con tag, preferita e riferimenti; combinare i filtri e avviare lo studio. Mostrare/nascondere il retro, cambiare scheda e mescolare controllando che la risposta torni nascosta. Nella demo scegliere chunk di fonti diverse, verificare le citazioni in anteprima, modificare/deselezionare/rimuovere proposte e importare solo la selezione. Riavviare e controllare contenuti, provenienza, conteggio e filtri; archiviare una fonte di prova e verificare che i riferimenti già salvati siano ancora leggibili.

Per una prova Windows isolata, la sola build **Debug** accetta la variabile di processo `STUDYSPACE_TEST_DATA_DIRECTORY` con un percorso assoluto. Avviare l'eseguibile da quel processo: l'app usa `studyspace.db3` nella directory scelta e il titolo finestra “StudySpace · Verifica”. Rimuovere la variabile dal processo al termine. La build Release ignora questo override; non impostarlo globalmente. Il database di prova non viene popolato automaticamente.

Le build e i test della logica non sostituiscono questa verifica interattiva su dispositivo.







