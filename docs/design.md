# Design for en published member cache i Umbraco 17 og 18

Status: Implementeret efter brugerens godkendelse 10. november 2026. Se [verifikation](verification.md) for aktuelle resultater og begrænsninger.

Pakken skal levere hurtige member-opslag som IPublishedContent, inklusive ModelsBuilder og property value converters. Den følger dokumentcachens arkitektur med indlæsning efter behov, serialiserbare data i HybridCache og færdige modeller i et lokalt lag. Load balancing er et krav fra første version.

Dette dokument bevarer designbeslutningerne og den historiske spike-undersøgelse. Den aktuelle pakkeimplementering og versionsbranches er beskrevet i repositoryets README og verifikationsrapport.

## Mål og afgrænsning

Besluttet:

- Understøt Umbraco fra 17.0.0 og fra 18.0.0, i separate versionsbranches.
- Lever en separat lookup-service til direkte opslag og en automatisk Member Picker-property value converter, som bruger samme cache.
- Understøt asynkrone enkelt- og batchopslag på numerisk ID og brugernavn.
- Returner IPublishedContent og den genererede ModelsBuilder-model, når den findes.
- Lad Umbracos egne property value converters konvertere properties, inklusive Block List.
- Indlæs medlemmer ved behov. Kræv ikke, at alle medlemmer ligger i hukommelsen.
- Invalider korrekt på tværs af servere.
- Tilbyd valgfri seeding efter member type og egne providers. Seeding er slået fra som standard og blokerer opstarten, når den er slået til.

Uden for første version:

- Opslag på email eller key; begge blev eksplicit taget ud.
- Synkrone opslag, generiske typede overloads, søgning og paging i det offentlige lookup-API.
- Automatisk ændring af MemberManager eller andre eksisterende Umbraco-opslag ud over Member Picker.
- Login, autorisation, ændring eller gemning af members, grupper og roller.
- Backoffice-dashboard, automatisk valg af hyppigt læste medlemmer og løbende baggrundsopvarmning.
- Egne property converters eller ændringer af Umbracos member-variationer.

Cachede approval-/lockout-felter er en published læseprojektion. Eksisterende Identity- og adgangskontrolforløb er fortsat ansvarlige for autorisation.

## Valgt tilgang og alternativer

En erstatning for IPublishedMemberCache alene blev undersøgt. Det eksisterende API tager et IMember, så databaseindlæsningen er allerede sket hos kaldere som Member Picker. Det er utilstrækkeligt til pakkens hovedformål.

Den valgte tilgang er en separat lookup-service som cachekerne med en tynd, automatisk Member Picker-converter ovenpå. En service alene kræver ændringer af templatekode og UDI-til-ID-håndtering i projektet. Converteren flytter denne integration ind i pakken, så normale Member Picker-kald bruger cachen uden eksplicit opt-in i Razor. Andre Umbraco-integrationer indgår ikke i første version.

For lagring blev ren datacaching med ny model ved hvert opslag og caching af færdige modeller undersøgt. Brugeren valgte dokumentcachens kombination: rådata i HybridCache og genbrug af færdige modeller lokalt. Serialisering af færdige ModelsBuilder-modeller indgår ikke.

## Repository og versioner

Besluttet: Repository følger brugerens umbraco-package-repository-standard. main retter sig mod seneste understøttede major, her Umbraco 18. v17/main retter sig mod Umbraco 17. Samme NuGet-ID bruges på begge branches, og pakkens major følger Umbracos major med 17.x og 18.x. Første stabile pakkeversioner er dermed 17.0.0 og 18.0.0.

Afhængighedsintervaller er [17.0.0,18.0.0) og [18.0.0,19.0.0). Begge branches bygger og udgives selvstændigt; fælles UmbracoMajor-property, conditional compilation og cross-major-projekt er ikke udgangspunktet. Den delte kode i spiken er alene et forsøgssetup og fastlægger ikke pakkens repositorystruktur.

Besluttede navne: repository umbraco-member-cache og pakke/projekt/namespace Umbraco.Community.MemberCache. Profilen er server-only med Microsoft.NET.Sdk, uden backoffice-klient eller custom Management API. Der foreslås appsettings-schema til pakkens cache- og seed-konfiguration.

```text
umbraco-member-cache/
  Umbraco.Community.MemberCache.slnx
  README.md
  LICENSE
  icon.png
  NuGet.Config
  .gitignore
  umbraco-marketplace.json
  umbraco-marketplace-readme.md
  src/Umbraco.Community.MemberCache/
    Umbraco.Community.MemberCache.csproj
    appsettings-schema.Umbraco.Community.MemberCache.json
    buildTransitive/Umbraco.Community.MemberCache.targets
  tests/Umbraco.Community.MemberCache.Tests/
    Umbraco.Community.MemberCache.Tests.csproj
  demo/Umbraco.Community.MemberCache.TestSite/
    Umbraco.Community.MemberCache.TestSite.csproj
  docs/nuget-readme.md
  .github/workflows/
    ci.yml
    publish.yml
```

Der er ét pakkeprojekt. Pakke, tests og demo indgår i løsningen; kun pakken er packable. Mindste nødvendige offentlige Umbraco-afhængighed vælges ved implementering, mens demo refererer til CMS. TFM og SDK verificeres for hver branchs minimumsversion.

Demoen skal genereres med den officielle Umbraco-template og bruge SQLite og project reference. Spikens håndskrevne apps må ikke erstatte demoen. Eksakt templateversion og kommando dokumenteres. Lokal database, logs, media og credentials holdes ude af Git og NuGet.

CI bygger løsningen, kører relevante tests, pakker pakkeprojektet og inspicerer den faktiske nupkg. Publishing validerer SemVer-tag, major, dependency-interval og commit-tilhørsforhold til den rigtige remote branch og bruger NuGet trusted publishing med OIDC. Der oprettes ingen remotes, tags eller releases som del af designarbejdet.

Ud over project-reference-demoen kræves installation af den byggede nupkg i en separat ren, templategenereret site på den deklarerede minimumsversion. Den første spike kørte på 17.8.1 og 18.0.0. Den efterfølgende benchmark-spike har også gennemført roundtrip-kontrollerne i 17.0.0; installation af en færdig nupkg er fortsat ikke verificeret.

## Ikon og farver

Besluttet: Ikonet i [icon-concept.png](brand/icon-original.png) og farvepaletten nedenfor er godkendt. Ikonet kombinerer en medlemssilhuet med stablede kort som symbol på cachede medlemsdata. Den godkendte udformning bevares ved klargøring af PNG-størrelser til NuGet/Marketplace. En [paletteoversigt](brand/palette.svg) og [brandnoter med genereringsprompt](brand/README.md) følger med.

Godkendt palette:

| Rolle | Farve | Hex |
|---|---|---|
| Base og mørk tekst | Mørk blågrøn | #142D38 |
| Primær brandfarve | Mint | #22C7A9 |
| Accent | Koral | #FF7964 |
| Lys baggrund og negativt rum | Varm elfenben | #F4F1E8 |

Farverne er brandfarver, ikke en færdig tilgængelig UI-palette. Kontrast og anvendelse i en eventuel senere UI skal verificeres til det konkrete formål. Pakken har ingen backoffice-klient i første version.

## Offentligt API

Foreslået navn: IPublishedMemberLookup. Navnet er ikke endeligt besluttet.

```csharp
public interface IPublishedMemberLookup
{
    Task<IPublishedContent?> GetByIdAsync(
        int id, CancellationToken cancellationToken = default);

    Task<IPublishedContent?> GetByUsernameAsync(
        string username, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IPublishedContent>> GetByIdsAsync(
        IEnumerable<int> ids, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IPublishedContent>> GetByUsernamesAsync(
        IEnumerable<string> usernames, CancellationToken cancellationToken = default);
}
```

Besluttet batchadfærd: Returner kun fundne medlemmer i inputrækkefølge. Et manglende enkeltopslag returnerer null.

Besluttet: Bevar inputdubletter som dokumentcachens nyere batch-API. Genbrug intern indlæsning af samme medlem, men gentag resultatet på hver matchende inputposition. Eksempel: input [42, 17, 42, 999] giver [member42, member17, member42], når 999 mangler. Der er stadig kun ét kanonisk cacheobjekt for medlem 42, selv om det optræder to gange i listen.

Implementerens standardvalg: Tomme batches giver en tom liste. Null-input og null/blankt brugernavn afvises. Ugyldige numeriske ID'er behandles som ikke-fundne. Username-normalisering og udvælgelse skal bevare IMemberService.GetByUsername-semantik frem for at antage, at en bestemt lowercasing svarer til databasens sammenligning.

## Cachelag og indlæsning

1. L0 er lokalt på hver server og indeholder færdige IPublishedContent-modeller.
2. L1 er HybridCaches lokale lag med serialiserbare snapshots.
3. L2 er et valgfrit delt lag gennem den eksisterende IDistributedCache-opsætning.
4. Ved miss hentes member-data gennem offentlige Umbraco-services.

Numerisk member-ID er den kanoniske cacheidentitet. Brugernavn peger på et ID, og alle opslag genbruger samme medlemsdata og model. Snapshot bevarer original Guid-key og email, fordi de stadig er en del af den published model, selv om opslag efter dem er fjernet.

Der bruges pakkespecifikke cachekeys, tags og en formatversion, så data ikke kolliderer med core eller andre installationer, og inkompatible formater ikke genbruges efter deployment. Partitioneringen skal respektere installationen og eventuelt miljøet ved deling af en cachebackend.

Besluttet: L0 har en konfigurerbar antalsgrænse med en standard på 10.000 færdige member-modeller pr. server. Dette er en antalsgrænse, ikke en grænse i megabytes, og standardværdien er endnu ikke belastningstestet. Dokumentcachens lazy loading betyder ikke automatisk bounded memory: core er som standard ubegrænset, og HybridCache-udløb rydder ikke selv L0. Eviction betyder, at næste opslag kan genskabe modellen fra HybridCache-data; det sletter ikke medlemmet fra databasen. Eviction-algoritmen overlades til implementeren.

Besluttet: HybridCaches udløbstider for rådata er som standard 1 dag lokalt og 365 dage i delt cache, begge konfigurerbare. Det gælder HybridCache-data, ikke automatisk L0-modeller. Seedede data bruger i første version samme varighed som øvrige member-data, uden en særskilt seed-TTL. Foreslået standard er ikke at cache resultatløse opslag. Udløb er en bagstopper; ændringer skal håndteres med invalidering. De valgte standarder følger dokumentcachen og er endnu ikke belastningstestet for pakken.

Batchforløbet undersøger cachehits først og indlæser kun misses, samlet hvor de offentlige Umbraco-API'er understøtter det. API'et lover ikke ét databasekald for en batch af brugernavne. Der må ikke startes ubegrænset parallel databaseadgang.

## Automatisk Member Picker integration

Besluttet: Første version indeholder en property value converter, der automatisk lader Member Picker bruge pakkens lookup-service. Almindelig templatekode bevares:

```csharp
var member = Model.Value<IPublishedContent>("member");
var title = member?.Value<string>("profileTitle");
```

Pakkens composer erstatter core-standardconverteren for Member Picker og registrerer pakkens converter som en standardconverter. Projektets egne custom converters skal fortsat kunne have forrang; pakken må ikke ukritisk fjerne projektdefinerede converters.

Adapteren håndterer de sourceformer, core understøtter i den relevante version, herunder legacy numeriske ID'er og member-UDI'er. En UDI oversættes internt til numerisk member-ID via IIdKeyMap, hvorefter den samme kanoniske cache bruges. Det udvider ikke det offentlige lookup-API med key-opslag. Ugyldige værdier, manglende medlemmer og version 18's understøttelse af eksterne medlemmer skal bevare cores relevante fallback-adfærd. Dette skal verificeres mod hver minimumsversion.

Besluttet: Converteren angiver PropertyCacheLevel.None, så ejerobjektet ikke fastholder en ekstra, gammel member-reference efter invalidering af den fælles member-cache. De færdige member-modeller genbruges fortsat i pakkens L0. At beholde Snapshot/Element-cache på picker-propertyen i 17 ville ellers kunne omgå pakkens invalidering ved efterfølgende læsninger. Cachelevel er en implementeringsdetalje og vises ikke i Marketplace-grafikken.

Versionspræcisering: [Umbraco PR #21006](https://github.com/umbraco/Umbraco-CMS/pull/21006) rettede Snapshot-til-Element-behandlingen allerede i 17.0.1 for at bevare dynamiske property-værdier. Benchmarkets cachede 17.0.0-resultat er derfor en før-rettelse-baseline og må ikke generaliseres til hele 17-serien. Den aktuelle converter-sammenligning bruger nu en verificeret 17.0.1-måling; senere patches er ikke målt her. Pakkens minimumsversion er fortsat 17.0.0.

Property value converter-API'et er synkront. Adapteren skal derfor kunne bruge et hurtigt varmt cachehit og afvente eventuelle kolde asynkrone opslag gennem en kontrolleret synkron bro. Der loves ikke et asynkront Razor-property-opslag eller nul blokering ved cachemiss. Service-API'et forbliver asynkront.

Returneret resultat er IPublishedContent med ModelsBuilder-wrapper, når en model findes. Eksisterende kode, som specifikt forventer IPublishedMember efter picker-konvertering, kan kræve tilpasning, da en genereret model ikke nødvendigvis implementerer dette interface.

En converter-prototype er nu implementeret i benchmark-spiken og registreret på fixture-aliaser via Umbracos normale PropertyValueConverters-collection. Den første undersøgelse målte core, None og Element gennem normale Value-kald i samme host. Efter valget af None måles kun core mod den valgte converter. Prototypekontrollen bekræfter, at None ser en udskiftet member-model på samme ejer, mens Element fastholder den gamle, indtil et nyt ejerobjekt bruges. Derfor er Element ikke alene en løsning, som samtidig opfylder kravet om opdatering på allerede læste ejerobjekter. Den færdige pakkes globale erstatning, fulde fallback-dækning, kolde opslag og distribueret invalidering er stadig ikke implementeret eller verificeret.

Den aktuelle sammenligning med den besluttede converter bruger 17.0.1/18.0.0: core 52,251/20,386 µs, vores converter 0,309/0,300 µs per gentagen picker- og tekstpropertylæsning. Det svarer til cirka 99,4%/98,5% mindre tid. 17.0.1 er målt på ny; 18.0.0-resultatet genbruges fra det afsluttede tovejsrun. Begge versioner bestod 11 roundtrip- og 10 converter-kontroller samt to Dry-cases før fem warmup- og 15 måleiterationer. Før/efter-grafikken viser den valgte adfærd uden cachelevel-navne. Den tidligere trevejsundersøgelse og 17.0.0-tovejsmåling er arkiveret under benchmark-spikens reports/cache-level-investigation/ og reports/umbraco17.0.0-converter/.

## Snapshots og genskabelse

Et snapshot gemmer den projektion, published mapping kræver, frem for hele IMember. Det omfatter identitet, member type-ID, navn, relevante datoer og statusfelter samt rå property-værdier. Passwordhash, security stamp og password configuration medtages ikke.

Raw properties genskabes eksplicit som int, decimal, DateTime, string eller null efter storage type. Block List bevares som sin oprindelige JSON-streng. En uspecificeret Dictionary<string, object>-roundtrip er ikke tilstrækkelig.

Genskabelsen bruger kun offentlige API'er:

```text
Snapshot + IMemberType
  -> new Member(memberType)
  -> offentlige setters og SetValue
  -> IPublishedMemberCache.GetAsync(member)
  -> IPublishedModelFactory.CreateModel(publishedMember)
  -> IPublishedContent
```

Type-definitioner skal også have passende caching og invalidering. Et varmt snapshot alene garanterer ikke nul databaseadgang, hvis typen er kold. Den genskabte Member er et privat mapping-input og udleveres ikke som en entity til Save.

ModelsBuilder-modellen implementerer ikke nødvendigvis IPublishedMember. Derfor lover API'et IPublishedContent; det underliggende objekt kan stadig være Umbracos PublishedMember. Den fremtidige pakke kræver ikke InMemoryAuto eller udviklingspakken i produktion. Den bruger projektets registrerede model factory og returnerer published modellen uden en genereret wrapper, når ingen model er registreret.

## Invalidering og load balancing

Brug de eksisterende distribuerede cache-refresher-notifikationer, som behandles på hver server. En handler på kun MemberSavedNotification er ikke tilstrækkelig.

- MemberCacheRefresherNotification invaliderer snapshots, L0-modeller og relevante username-opslag ved save, rename og delete samt fuld refresh.
- Payload-ID gør det muligt at invalidere et slettet medlem uden at genhente dets key. Current/previous username bruges, når de findes. Ved mindre detaljerede beskeder skal username-cache kunne ryddes konservativt.
- ContentTypeCacheRefresherNotification og DataTypeCacheRefresherNotification håndteres også. Cachede member-modeller og snapshots må ikke fortsætte med gamle definitioner.
- Live ModelsBuilder-reset kan ændre hele generationen af CLR-typer. Et konservativt udgangspunkt er at rydde pakkens samlede L0 på schema-/datatype-refresh, også når den direkte ændring ligger uden for member types.
- IndexableFieldsChanged er et indeksflag og bruges ikke alene til at springe member-invalidering over; published members eksponerer også login-/kontodata.

Implementerens forslag efter gennemlæsning: ContentCacheRefresherNotification og MediaCacheRefresherNotification rydder også pakkens samlede L0, mens rå member-snapshots bevares. Et member kan have en Content Picker eller Block List med referencer til disse entiteter. Ellers kan allerede konverterede værdier holde på gamle refererede objekter efter en ændring. Den konservative rydning undgår afhængighedssporing i første version, men kan give ekstra modelkonvertering på sites med mange content-/mediaændringer. Næste opslag skal følge den respektive servers opdaterede published caches.

Dette er ikke en garanti for vilkårlige eksterne datakilder i custom converters. Sådanne converters skal vælge passende cachelevel og selv håndtere deres eksterne datakilders levetid/invalidering. Pakken ændrer ikke deres kontrakt eller tilføjer generel afhængighedssporing.

Konsistens følger Umbracos eksisterende eventual consistency. Andre servere kan returnere gamle data, indtil de har behandlet refresh-instruktionen. Allerede returnerede objekter kan fortsat bruges af et igangværende request. Pakken lover ikke en global transaktion eller øjeblikkelig ændring på alle servere.

Igangværende indlæsning og seeding må ikke efterlade gamle snapshots, modeller eller username-mappings permanent genindsat efter invalidering. Beskyttelsen skal omfatte alle cachelag og testes med et delt cachelag. En lokal generationstæller er et relevant mønster fra core, men er ikke i sig selv en distribueret versionsgaranti. Det præcise locking-/generation-/eviction-forløb overlades til implementeren under dette krav.

## Seeding

Besluttet kontrakt:

```csharp
public interface IMemberSeedProvider
{
    IAsyncEnumerable<int> GetMemberIdsAsync(
        CancellationToken cancellationToken = default);
}
```

Provideren udvælger ID'er. Pakken indlæser data, håndterer batches og udfylder HybridCache. Provideren opbygger ikke published modeller og skriver ikke selv cacheposter.

Seeding er slået fra som standard. Når den er slået til, gennemføres den under opstart før applikationen er klar. Den kører ikke under installation eller i en upassende upgrade-tilstand.

En indbygget MemberTypeSeedProvider udvælger medlemmer fra konfigurerede member types. Implementerens foreslåede standard er member type-aliaser i konfigurationen, valideret ved opstart. Egne providers registreres med DI. Flere providers kombineres, ID'er deduplikeres, og data behandles i batches. Foreslået standardbatchstørrelse er 100, konfigurerbar. Disse standarder er forslag, ikke tidligere godkendte værdier.

Providerne skal kunne enumerere med cancellation og paging. De behøver ikke returnere alle ID'er samlet. Det samlede deduplikeringssæt kan dog stadig vokse med antallet af seed-ID'er og må ikke beskrives som konstant hukommelsesforbrug.

Seed kun rådata i HybridCache. L0 og konverterede properties varmes ved normale opslag. Gyldige cacheposter springes over. Seedede medlemmer invalideres som øvrige medlemmer og er ikke automatisk permanent fastholdt i L0.

Hver server gennemfører sin lokale startup-seeding. Et fælles L2 kan mindske gentagen databaseindlæsning, men varmer ikke automatisk andre serveres lokale cache. Samtidig opstart kan give gentagne indlæsninger; første version lover ikke en distribueret single-flight/lås. Det skal begrænses med batches og korrekt koordinering med invalidering.

Implementerens forslag: Resolve providers i et oprettet DI-scope frem for at kræve singleton. Det tidligere singleton-eksempel var en mulighed, ikke en bindende lifetime. Dermed kan en custom provider bruge scoped services uden root-resolution.

Besluttet fejladfærd: En seed-fejl stopper opstarten. Fejlen logges og sendes videre ud af startup-forløbet; der faldes ikke tilbage til lazy loading med successfuld readiness. Allerede skrevne cacheposter behøver ikke rulles tilbage, men må ikke forhindre næste startup-forsøg i at gennemføre seeding. Når seeding er deaktiveret, må hverken den indbyggede eller custom providers enumereres. Periodisk reseeding indgår ikke. Manuel fuld rydning kan i første version lade cachen blive genopbygget ved opslag frem for automatisk at blokere og reseede.

## Fejlhåndtering og implementerens råderum

Implementeren vælger interne klasser, serializer-/eviction-mekanik, normal fejllogning og pakkeopdeling for 17 og 18. Der bygges ikke et generelt cacheframework. Navn og NuGet-identitet fastlægges før publicering.

Konfigurationsfejl, som ukendte seed-member types eller ugyldige kapaciteter, opdages tydeligt. Deserialiserings-/formatfejl kan medføre eviction og én genindlæsning fra kilden. Reelle kildefejl må ikke omdannes til et falsk ikke-fundet-resultat. Cancellation skal respekteres ved indlæsning og seeding; core-API'er uden cancellation må ikke fremstilles som fuldt afbrydelige.

## Verificeret og endnu ikke verificeret

Spiken i [member-cache-spike](benchmarks/prototype-method.md) har 11 beståede kontroller i hver af Umbraco 17.8.1 og 18.0.0. Den bruger rigtige SQLite-databaser, offentlige services og faktisk generering af member- og elementmodeller gennem ModelsBuilder InMemoryAuto.

Det er verificeret, at JSON-roundtrip og ny Member-instans bevarer ID/key, råværdier, decimal, dato, null og Block List-JSON; IPublishedMemberCache opbygger published member; model factoryen skaber Spike.Generated.SpikeMember; og den genererede Blocks-property giver BlockListModel med en Spike.Generated.SpikeBlock og korrekt Heading-værdi.

Resultater: [17.8.1](benchmarks/spike-umbraco17.json) og [18.0.0](benchmarks/spike-umbraco18.json). Koden er en throwaway spike, ikke begyndelsen på produktionspakken.

Endnu ikke testet: HybridCache/L2, flere servere, samtidige writes, invalidation, eviction, providers/startup-seeding, schemaændringer, live-regenerering, alternative ModelsBuilder-modes og custom converters.

En separat [benchmark-spike](benchmarks/prototype-method.md) har målt både direkte IPublishedMemberCache-konvertering og Member Picker i minimumsversion 17.0.0 og 18.0.0. Den primære sammenligning giver core et allerede indlæst IMember og bruger IPublishedMemberCache.GetAsync + ModelsBuilder som baseline. Varm modelgenbrug tager cirka 0,057/0,058 µs mod cores 13,901/3,674 µs i henholdsvis 17/18. Databaseopslag og propertylæsning indgår ikke. Genskabelse fra varme rådata tager 32,899/20,250 µs, så denne CPU-rute er dyrere end at konvertere et allerede indlæst member. Identitet, værdier og genereret modeltype er verificeret før måling. Den tidligere picker-måling viser en gevinst i 18, men at 17 allerede cacher picker-resultatet på objektet. Resultaterne er eksplorative og må ikke fremstilles som benchmarks af en færdig pakke eller som en generel forbedring af sidetider.

Forsøget viser også et integrationskrav: En moderne Member Picker gemmer en UDI/key. Den besluttede automatiske converter læser sourceværdien og oversætter UDI til ID via IIdKeyMap internt, før lookup-servicen kaldes. Projektets Razor behøver dermed ikke gøre dette. Den benchmarkede eksplicitte prototype illustrerer cachekernen, men må ikke fremstilles som en måling af den kommende converter.

Den efterfølgende fulde ID-opslagsmåling inkluderer IMemberService.GetById, IPublishedMemberCache og ModelsBuilder inden for hver core-operation. Core tager cirka 47,074/16,471 µs i 17.0.0/18.0.0; varm modelgenbrug tager 0,057/0,065 µs og rådata-genskabelse 23,803/21,469 µs. Begge versioner består tre sammenligningsforløb med samme identitet, værdi og genererede modeltype samt roundtrip-kontrollerne. Core beholder sine normale caches. Forsøget bruger gentagne opslag på ét ID i en lokal SQLite-host og dækker ikke kolde opslag, store datasæt, L2, invalidering eller færdig pakke. Det primære benchmark-screenshot viser nu dette hele opslag; de tidligere målinger er bevaret særskilt.

Der er udarbejdet [fire Marketplace-screenshots](screenshots/benchmark-results.png), inklusive den registrerede converters sammenligning, og en schema-valideret [umbraco-marketplace.json](../umbraco-marketplace.json) med billed-URL'er under https://github.com/skttl/umbraco-member-cache. Manifest og billeder er lokale leverancer; offentlige URL'er virker først efter placering i repositoryets main-branch. Repositoryets remote havde ingen Git-refs ved kontrollen. Metadata og billeder skal opdateres efter måling af den færdige pakke før release.

Første implementationsplan skal inkludere test af:

- Enkeltopslag og batches med ID/brugernavn, rækkefølge, manglende medlemmer, tomme inputs og bevarelse af inputdubletter uden gentagen indlæsning af samme medlem.
- Automatisk Member Picker-konvertering uden templateændringer, legacy ID og member-UDI, manglende/ugyldige værdier, version 18's eksterne fallback samt projektdefinerede converters.
- En allerede læst Member Picker skal efter member-invalidering få det nye resultat frem for at holde på en gammel reference. Mål også den automatiske converters faktiske performance i begge minimumsversioner.
- Cachehits, kolde serializer-roundtrips, håndhævelse af L0-grænsen og eviction samt de konfigurerbare standardudløb på 1 dag lokalt og 365 dage i delt cache.
- Save, rename, delete og fuld refresh på server A efterfulgt af opslag på server B.
- Indlæsning og seeding, der overlapper med invalidering.
- Member type-/datatypeændring og ModelsBuilder-reset.
- Content-/mediaændringer, som et allerede konverteret member refererer til, også inde i Block List.
- Deaktiveret seeding, valgte typer, flere/custom providers, dubletter, cancellation og valgt fejladfærd.
- Begge majorversioner og de aftalte minimumsversioner.

## Åbne valg før implementationsplan

License vælges inden offentliggørelse og må ikke overtages fra et vilkårligt referenceprojekt. Ikon og farver er godkendt; de endelige PNG-størrelser og NuGet-klargøring mangler.

Brugeren har godkendt implementeringen i det angivne repository. MIT er valgt til den første version; ikonets NuGet-udgave er klargjort som 128×128 PNG.

## Gennemlæsning

En separat gennemlæsning af det første udkast fandt åbne valg om minimumsversioner, retention, seed-fejl og batchdubletter samt manglende håndtering af refereret content/media. Brugeren har siden fastlagt minimumsversioner, branchstandard, fail-fast ved seed-fejl, en standardgrænse på 10.000 lokale modeller, udløbstider på 1/365 dage og bevarelse af batchdubletter. Refereret content/media er indarbejdet som et eksplicit forslag om konservativ L0-rydning. Det er en designkontrol, ikke en runtime-verificering af de endnu ikke implementerede cacheforløb.
