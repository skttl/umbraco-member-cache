# Prototype benchmark history

The initial design was investigated in throwaway Umbraco hosts using SQLite, actual ModelsBuilder models and Block List properties. The early cache was a small ConcurrentDictionary over HybridCache and had no production invalidation, capacity enforcement or source freshness safeguards. Images produced during that investigation must not be presented as measurements of the finished package.

The last prototype converter comparison used normal Value calls on a member-owned picker fixture: core versus a registered converter that resolved a warm model. Umbraco 17.0.1 measured 52.251 µs versus 0.309 µs; 18.0.0 measured 20.386 µs versus 0.300 µs. The 17.0.0 cache behavior was separately investigated and predates [Umbraco's 17.0.1 correctness fix](https://github.com/umbraco/Umbraco-CMS/pull/21006).

The historical rehydration checks used public Member setters, IPublishedMemberCache and IPublishedModelFactory. Their reports are preserved as spike-umbraco17.json and spike-umbraco18.json. The production package has separate unit tests, real document-owner runtime checks and package-install verification. Finished-package benchmark reports, when available, are under package/ and explicitly identify their versions and methodology.
