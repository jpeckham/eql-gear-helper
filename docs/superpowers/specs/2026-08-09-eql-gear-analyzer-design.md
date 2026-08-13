# EQL Gear Analyzer Refactor Design

## Purpose

Refactor the existing WPF item lookup utility into the local-first EQL Gear
Analyzer defined by `EQL_Gear_Analyzer_PRD_UX.md`. The application must answer
what to keep and what to obtain for an EQL three-class build without reducing
assets to isolated item scores.

## Architectural boundaries

The solution is divided into four projects with dependencies directed inward:

```text
EqlGearHelper.Wpf -> EqlGearHelper.Application -> EqlGearHelper.Domain
EqlGearHelper.Infrastructure -> EqlGearHelper.Application
EqlGearHelper.Infrastructure -> EqlGearHelper.Domain
```

- **Domain** contains no WPF, SQLite, filesystem, HTTP, or persistence DTOs.
  It contains immutable entities and value objects for catalog items, owned
  instances, Exaltations, class sets, positions, loadouts, rulesets,
  assessments, actions, and confidence.
- **Application** owns use cases and ports for catalog import, inventory import,
  collection coverage, single-trio planning, collection-wide cleanup, export,
  backup, and recovery.
- **Infrastructure** implements the application ports through SQLite, catalog
  package import, inventory-file parsing, backup files, and an optional external
  EQL Legends Tools enrichment adapter.
- **Wpf** owns navigation, views, controllers, presenters, and view models. It
  invokes application use cases but contains no business or persistence rules.

## Catalog, provenance, and ruleset decision

SQLite contains the authoritative local catalog, imported from a versioned
package. The package supplies canonical item facts, Exaltation definitions,
acquisition routes, race and class eligibility, and source/version attribution.
Catalog identity is independent from an owned item's displayed upgrade or
Exaltation suffix because the inventory export can use the same numeric item ID
for multiple owned variants.

Catalog acquisition data is evidence-backed and retains both normalized facts
and the raw evidence used to derive them. The enrichment chain is:

1. public EQ Legends Tools item HTML;
2. EQLWiki item-template fields such as `dropsfrom`, `relatedquests`, and
   `playercrafted`;
3. EQLWiki categories, links, and backlinks to quest, NPC, and zone pages; and
4. classic-era sources such as ZAM, PQDI, P99, EQProgression, or FV Project as
   corroboration only, because their rules may differ from EQ Legends.

The EQ Legends MCP's zone/map records do not constitute item-to-zone data.
Plane of Sky, Plane of Fear, quest, and crafted classifications are normalized
acquisition-route tags derived from item evidence, not inferred from item names
or assumed to be native zone fields. Each normalized route records its type,
zones, description, source URL, raw evidence, and confidence. A source is marked
unresolved only after the enrichment chain cannot establish a route. Missing or
conflicting enrichment can never produce a confident disposal recommendation,
but unresolved acquisition data alone does not exclude a compatible item from
Auto-BiS.

A versioned ruleset supplies the class universe, legal trios, position schema,
effect stacking/tiering, utility curves, materiality tolerance, and target
policy. The schema preserves Wrist and Bracer as distinct requirements until
verified rules explicitly relate them.

Ruleset values that remain unverified (stat caps, effect stacking, weapon
evaluation, class legality, or socket mapping) are displayed with their source
and confidence. They must not be silently replaced by assumptions. A missing or
unresolved rule blocks only the affected high-confidence conclusion, never
causes data loss or a plain disposal recommendation.

## Inventory and collection model

An inventory import creates a snapshot of physical owned item instances. Each
copy retains catalog identity, upgrade level, full hierarchical location, and
installed subordinate socket rows. Unknown catalog facts and socket mappings
are preserved rather than inferred. They are displayed in a resolution queue
and block complete-confidence disposal.

Manual records extend coverage for Dragon Hoard, Item Storage, and Exaltation
Storage. Coverage explicitly distinguishes imported, manually maintained,
unavailable, empty, and incomplete sources.

The Inventory view groups equivalent owned variants into one expandable row
with total quantity and locations while retaining every physical instance for
assignment and cleanup analysis. Equivalent means the same canonical item,
upgrade level, Exaltation state, and other loadout-relevant configuration.
Containers and consumable stacks are not treated as equippable duplicate gear.

A duplicate warning means the group exceeds its maximum simultaneous equipment
capacity, not that the excess copies are automatically disposable. The initial
capacity is two for rings, earrings, wrists, and compatible one-handed weapons,
and one for ordinary single-position equipment. The assignment engine may
lower a weapon's useful capacity when hand, lore, class, or other restrictions
prevent two simultaneous copies. Cleanup makes the final keep/dispose decision.

The `Parnell_oggok-Inventory.txt` fixture is a parser contract. The importer
must recognize its header and every data row; preserve equipped, General, Bank,
Shared Bank, and nested paths; retain individual duplicate copies; associate
`Polished Mithril Mask +4` with its native subordinate Exaltation; preserve the
transferred Exaltation and both IDs for `Pristine Studded Leather Boots +4`; and
retain distinct socket states for the two `Short Sword of the Ykesha +4`
instances. A failed import never replaces a prior complete snapshot. Dragon
Hoard, Item Storage, and Exaltation Storage are reported unavailable, not empty.

## Shared eligibility, scoring, and assignment engine

`PositionSchema` defines concrete equipment positions, including Ear 1/2,
Ring 1/2, Wrist 1/2, Bracer 1/2, and Any 1/2. A deterministic,
cancellation-aware assignment service builds complete valid loadouts. It honors
physical-copy uniqueness, duplicate-position requirements, lore/unique rules,
two-handed weapon conflicts, class eligibility, and Any-position eligibility.

Installed Exaltations narrow the host's effective class set through set
intersection. An empty intersection invalidates the configuration. All stat
and effect valuation occurs after a complete loadout is assembled:

- DEX has zero optimization utility.
- CHA applies only to applicable builds and only below the configured target.
- HP, mana, endurance, AC, primary damage stats, and resists use explicit
  ruleset thresholds and diminishing utility.
- Key effects use stack groups, tiers, and required coverage. A sufficient
  non-stacking source suppresses duplicate coverage value.

The selected race contributes the same two kinds of behavior exposed by the EQ
Legends character sheet: race-dependent base-stat/scoring effects and item-race
eligibility. Class, race, slot, hand, lore, Exaltation, physical-copy, and
upgrade legality are shared by Best Available, Goal State, and Cleanup so the
three workflows cannot disagree about whether an item can be used.

## Use cases

`BuildPlanner` accepts one selected race, exactly three distinct legal classes,
an upgrade setting, priority-stat checkboxes, and acquisition-filter
checkboxes. Every concrete equipment position displays three values side by
side:

- **Current State**: the item presently equipped according to the imported
  inventory snapshot;
- **Best Available**: the highest-scoring compatible assignment using only
  physical copies found anywhere in the `/outputfile inventory` data, including
  equipped slots, bags, bank, shared bank, and nested containers; and
- **Goal State**: the editable catalog target loadout.

Best Available ignores acquisition filters because the character already owns
those items. Goal State initially remains unchanged until the user invokes
Auto-BiS. Auto-BiS fills the Goal State with the best compatible catalog items
for the selected race, trio, upgrade, and priority-stat checkboxes. A manual
Goal State substitution may choose a lower-scoring compatible item and changes
only that position; it never re-optimizes the rest of the build.

Acquisition filters affect only Auto-BiS candidate selection. The initial
filters are Plane of Sky, Plane of Fear, quest rewards, and player-crafted
items. Filters operate per route:

- an item with at least one known unfiltered route remains Auto-BiS eligible;
- an item whose every known route is filtered is skipped by Auto-BiS;
- an item with no resolved route remains Auto-BiS eligible and is visibly
  marked `Source unresolved`; and
- an item illegal for the selected race, classes, slot, or configuration is
  never eligible.

Filtered and unresolved items remain visible and manually selectable in the
Goal State dropdown. Rows use a visual designation, and hover details list all
routes, the filtered reason for each affected route, evidence/provenance, and
whether an allowed route remains.

`CleanupAnalyzer` evaluates every legal race and three-class combination without
requiring the Build Planner selection. It reuses the shared engine and evaluates
each physical asset separately for base usefulness, redundancy, preservation
reasons, final action, and confidence. An item is useless only when no legal
build has a materially useful need for that copy. Valuable native/installed
Exaltations and unresolved eligibility, identity, or effect data protect an
item from plain disposal. Only eligible candidates appear in a location-sorted
disposal export. Unresolved acquisition data does not by itself protect or
condemn an already-owned item because acquisition is irrelevant after ownership.

Final action is derived after, not instead of, base usefulness and preservation.
The only actions are Keep, Needs Review, Extract Then Dispose, and Dispose
Candidate. `Exaltation Source` is a preservation reason and display facet, not a
mutually exclusive action. A candidate is disposable only when no materially
competitive legal trio or specialized loadout needs that physical copy, its
duplicate/Any-position uses are covered, and no valuable or unresolved
Exaltation remains.

## WPF experience

The WPF shell provides Cleanup, Inventory, Build Planner, Exaltations, Data,
and Settings. Cleanup follows the provided design concept with assessment
filters, coverage, summaries, item table, and a full explanation panel. Build
Planner displays trio selection, requirement coverage, complete concrete slot
assignments, target gaps, and recommended acquisitions. All views explicitly
represent empty, loading, failed, stale, partial, and blocked states.

Cleanup mirrors the supplied hierarchy: title and all-trio explanation; search;
Import, Reanalyze, and Export controls; summary cards; status and source filters;
location tree; owned-item table; and an item-analysis panel. Selecting an item
reveals its action, confidence, class/effect/slot facts, preservation reasons,
representative trio-position uses, comparable owned alternatives, and a
human-readable explanation.

Build Planner follows the interaction model of the EQ Legends Tools character
sheet while adding owned-inventory comparison. It provides race and three-class
selectors, upgrade and priority-stat controls, acquisition filters, Auto-BiS,
and a concrete slot grid containing Current State, Best Available, and editable
Goal State. The selected-item details show score contribution, legality,
location when owned, acquisition routes, filter status, and provenance. The
supplied screenshots and reference site govern visual hierarchy and interaction
density only; their item facts and assignments are not domain data.

Before connecting these views to application services, a separate WPF preview
project presents Build Planner, Cleanup, and Inventory with mock data and no
production backend. Native tabs, checkboxes, dropdowns, expandable inventory
groups, and tooltips are interactive; analysis/import buttons are visual only.
The preview is the visual-approval artifact and is kept isolated from production
composition so mock behavior cannot leak into the application.

## Persistence, recovery, and observability

SQLite persists catalog versions, rulesets, collection profile, snapshots,
manual storage, analysis inputs, and migrations. Backup/export and recovery are
application use cases. Results display catalog version/source attribution,
ruleset version, snapshot identity, coverage state, and diagnostics.

## Verification

Domain and application tests have no WPF, SQLite, HTTP, or filesystem
dependencies. Tests cover the supplied inventory fixture, nested storage,
transferred Exaltations, duplicate and Any positions, grouped presentation with
instance preservation, DEX/CHA rules, race and class eligibility, effect
stacking, class intersections, donor protection, collection-wide all-race and
all-trio cleanup, manual Goal substitutions without re-optimization, Auto-BiS
priority scoring, per-route acquisition filters, unresolved-source eligibility,
source enrichment/provenance, unknown-data disposal blocking, and Clean
Architecture boundaries.

After every implementation change, verification runs `dotnet build`, then
`dotnet test`, then launches the WPF executable and confirms that its process
remains running through startup initialization. Completion is not reported
unless all three checks pass or a specific environmental limitation is stated.

Acceptance traceability is mandatory:

- AC-01 is proved by fixture parser and snapshot-rollback tests.
- AC-02 through AC-04 are proved by collection-wide, duplicate-position, and
  Any-position optimization tests.
- AC-05 through AC-10 are proved by whole-set valuation, effect, compatibility,
  and Exaltation-preservation tests.
- AC-11 through AC-13 are proved by selected-trio, gap, source, and target-policy
  tests.
- AC-14 is proved by unresolved catalog/Exaltation and disposal-export tests.
- AC-15 is proved by project-reference and architecture tests that reject WPF,
  SQLite, HTTP, and filesystem dependencies from Domain and Application.

## Delivery order

1. Build and visually approve the zero-backend WPF preview for Build Planner,
   Cleanup, and Inventory.
2. Extend catalog facts, provenance, acquisition routes, race restrictions, and
   EQ Legends Tools/EQLWiki enrichment.
3. Complete shared eligibility, scoring, physical assignment, and duplicate
   capacity rules.
4. Implement Current State, Best Available, editable Goal State, Auto-BiS, and
   route-level filters.
5. Implement grouped inventory and collection-wide every-race/every-trio cleanup.
6. Connect the approved WPF views, complete data operations and recovery, and
   run the acceptance audit.
