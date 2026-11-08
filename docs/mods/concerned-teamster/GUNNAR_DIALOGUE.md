# Gunnar dialogue (#459): source handoff

29 original rotating remarks plus introduction/objective/completion and an
attention template are supplied as stable localization rows. GunnarTalk uses the
shared DialogueCatalog/DialogueRotation with an eight-line suppression window.
Only Interact speaks; there is no timer, update speech or ambient callback.
IntroPending says the real no-cart objective; recruited dialogue never invents
cargo, numeric load limits, history, a biome visit or a boss encounter.

Attention speech reads the SAME HaulPanelKeys.ForReason and TeamsterStrings row as
the real panel, only while its live phase is NeedsAttention. Tests walk every
HaulAttentionReason. Critical attention facts intentionally repeat on request;
the noncritical authored corpus rotates. No reason is inferred from appearance.

No read-only local-character knowledge signal for Hulgi/Thorstein or the
metal-out-of-the-forest hint was found in the current capability contracts.
Those lines are absent/ineligible. Presence of a plugin would not prove meeting
its companion. No global boss keys, another player's discoveries, product
assembly references or invented evidence are used.

Claude's #495 actively owns TeamsterStrings.cs. This branch therefore leaves it
untouched: the lead must append GunnarDialogueStrings.English rows to its English
catalog before translator template/override parsing runs. The source rows already
provide a fallback; translator callbacks are accepted, but current Teamster's
translator file will NOT accept these new keys until that catalog merge. This
required #459 acceptance row remains pending, not silently claimed complete.

Lead: create a GunnarTalk per character/world; the figure's talk callback supplies
CURRENT quest stage/haul phase/attention and TeamsterStrings.Get after merging
rows. Display GunnarDialogueStrings intro once during #457's interaction,
objective while pending, and completion only AFTER #460's durable recruitment.
Keep utility gates and worker switches separate. No plugin/panel/worker/validator
or game changes were made here.

Automated: catalog size/uniqueness/translation rows, three rotations with recency
suppression, every actual attention reason and panel key, unknown reason refusal,
objective/tools-only behavior, translation use and withheld knowledge hints.
OWNER GO-AROUND PENDING: real use-key talk, localizable names/lines after catalog
merge, reason changing with the running haul, repetition/voice feel and no ambient
speech across relog. The figure (#458) exists; binding its talk point is this issue's.
