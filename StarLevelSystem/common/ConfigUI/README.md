# In-game config UI

> **This folder is a copy of `Common/Config/UI/` from JotunnTemplatePlugin.** Keep it textually identical
> to the original apart from the namespace line, so the copies can be diffed against each other. The
> `Examples/` file referenced below was not copied; this mod registers its own panel.
A widget kit for building config panels out of Jotunn's `GUIManager` primitives, plus a **shared
launcher**: one button bottom-right of the main and pause menus that lists every loaded mod which has
registered a panel.

## Dependency rule

**Nothing in this folder may reference `YamlConfigFile`, `YamlConfigManager`, `ConfigNetwork` or
`ValidationReport`.** Its only in-repo dependency is `Logger`.

That is not tidiness — it is what lets a mod with a completely different config system take this folder
and join the shared launcher without also swallowing the YAML framework next door. Keep it true.

## Registering a panel

```csharp
internal static void Init() {
    ConfigUILauncher.Init();
    ApplyRegistration();
}

internal static void ApplyRegistration() {
    if (ValConfig.ShowQuickConfigButton.Value) { ConfigUILauncher.Register("MyMod", OpenPanel); }
    else { ConfigUILauncher.Unregister("MyMod"); }
}
```

Call `Init()` from `Awake`. Wire `ShowQuickConfigButton.SettingChanged` to `ApplyRegistration` so the
entry can be turned off without a restart. With exactly one mod registered the button opens that panel
directly instead of showing a one-item list.

The button is visible **only to a host or a server admin** — a remote non-admin's edits would be
overwritten by the next server broadcast, so offering the editor at all would be a lie. Visibility
re-evaluates on `OnAdminStatusChanged`, so it appears when admin status arrives without a relog.

## Where the button appears

**Main menu and pause menu, never the in-game HUD.** Two GameObjects, one per parent, and exactly one of
them is ever on screen:

| Parent | Shown when |
| --- | --- |
| `GUIManager.CustomGUIFront` | `FejdStartup.instance != null`, i.e. the `start` scene |
| `Menu.m_root` | Valheim shows the pause menu — `m_root` is toggled by the game, so this follows for free |

The `FejdStartup` gate is load-bearing. Jotunn **rebuilds `CustomGUIFront` on every scene change**
(`GUIManager.TryCreateGUI`, wired to `SceneManager.sceneLoaded`), so without it the `main` scene grows its
own copy of the button: one floating over the HUD, and a second one beside the pause menu's, at a
different position because `CustomGUIFront` carries its own `Canvas` and `CanvasScaler`.

Both parents route through the single `EnsureCornerButton(existing, parent)`. It bails when the button it
already holds is still parented to `parent`, then falls back to `parent.Find(ButtonObjectName)` and adopts
that if present, and only creates as a last resort — so `OnCustomGUIAvailable` and the `Menu.Start`
postfix firing repeatedly cannot stack buttons.

The mod list itself closes on its top-right `X` or on **Escape**. In-world that goes through a prefix on
`Menu.Update`, which closes the list and returns `false` for that one frame: Valheim reads Escape inline
there, so swallowing the frame is the only way to keep the press from also collapsing the pause menu the
list was opened from. One press dismisses the list, a second dismisses the menu. In the start scene there
is no `Menu`, so the broker's own `Update` handles it and no suppression is needed.

## The frozen cross-assembly contract

Every mod compiles its **own** `QuickConfigBroker`, so those types are unrelated as far as the CLR is
concerned and no cast between them can ever work. The first copy to run creates a `DontDestroyOnLoad`
GameObject named `ModQuickConfigLauncher`; every copy after that finds it and calls into whichever broker
is already there **by reflection**, binding `Register(string, Action)` by exact signature. Only BCL types
cross the boundary.

```csharp
internal const string BrokerObjectName = "ModQuickConfigLauncher";
internal const string BrokerTypeName   = "QuickConfigBroker";
internal const string ButtonObjectName = "ModQuickConfigButton";
internal const int    ContractVersion  = 2;

public int  BrokerVersion { get; }
public void Register(string modName, Action openPanel);
public void Unregister(string modName);
public bool IsRegistered(string modName);
```

**Amendment rules: additive only.** Never rename a member, reorder or retype a parameter, add a
same-arity overload, or narrow visibility. A newer caller probes with `GetMethod(...) != null` and
degrades silently. A genuine breaking change would need a *new* `BrokerObjectName`, i.e. two buttons on
screen during the transition — so do not make one.

**First broker to create the GameObject wins.** Version mismatches are advisory and logged once at Info,
naming the owning assembly; registration never refuses. The accepted cost is that an old copy inside an
unrelated mod pins the launcher UI at an old version. The alternative — handing the launcher over to a
newer copy mid-session — would leave every other assembly's cached `MethodInfo` pointing at a retired
component, which fails silently and much worse.

## Widgets

Layout is **top-left origin**: `anchorMin = anchorMax = pivot = (0,1)`, `anchoredPosition = (x, -y)`.
Build a `List<GameObject>` of rows in reading order, then call `LayoutColumn(rows, x, startY)` once. It
skips inactive rows, so `SetActive(false)` on a conditional row collapses its space — re-run it after any
visibility change to reflow.

| Widget | Use for |
| --- | --- |
| `AddToggleRow` | bool |
| `AddSliderRow` | int/float — slider plus a typed box, bound both ways and clamped |
| `AddEnumCycleRow` | an enum with **≤ 6** members |
| `AddPickerRow` | an enum with more, or any open-ended name (prefabs) |
| `AddEnumFlagsRow` | a small enum used as a set |
| `AddTextFieldRow` | free text |
| `AddStringListEditor` | `List<string>` with add/remove |

`AddCloseX(panel, panelWidth, onClick)` puts a dismiss button in a panel's top-right corner. Prefer it to
a full-width "Close" at the bottom: panel titles are centred so the corner is free, whereas a bottom
button has to be laid out around whatever the last row turns out to be.

Rows inside a `CreateScroll` must use `NewLayoutRow`, not `NewRow`: Jotunn's scroll content carries a
`VerticalLayoutGroup` that overwrites `anchoredPosition`, so those rows size themselves through a
`LayoutElement` and must not be passed to `LayoutColumn`.

**Do not put a Unity `Dropdown` in a Valheim scroll view.** `GUIManager.CreateDropDown` exists, but the
option list is instantiated as a child of the dropdown's own root, and the viewport's `Mask` clips it —
the popup is simply invisible. `ConfigUIPicker` parents to `CustomGUIFront` instead, and its filter box
makes it usable for lists a dropdown never could be.

`AddToggle` carries a workaround for a real Jotunn bug: `CreateToggle` parents with
`SetParent(parent)` and no `worldPositionStays: false`, which corrupts the toggle's scale. **Never call
`CreateToggle` directly** — go through `AddToggle`.

## Input blocking

Every `InputField` in a Valheim UI leaks keystrokes into the game. `CreatePanel` attaches a
`ConfigUIInputGuard` that takes a refcounted `GUIManager.BlockInput` and releases it from `OnDestroy`.
The release is tied to the component's lifecycle **on purpose**: a close-handler release does not run if
an exception is thrown mid-build or the scene changes, and the player is then stuck unable to move with
no way out but a relog.

## Localization

`AddText`, `AddButton` and the picker run their labels through `Localization.instance.Localize`, so pass
`$tokens` for anything your mod owns. Enum *member names* are deliberately left raw — they are the tokens
an admin types into a YAML file, and showing a translated form would teach them the wrong word.

The kit's own strings ("Close", "Add", "Filter…") are plain English literals, not tokens, so the folder
renders correctly when dropped into a mod that has no localization set up at all.

## Dropping this into another mod

Copy `Common/Config/UI/`, then:

1. Make sure your `Logger` exposes `LogDebug` / `LogInfo` / `LogWarning` / `LogError`.
2. Add a `ConfigEntry<bool> ShowQuickConfigButton` (Client config, default true, **not** `IsAdminOnly` —
   it is a per-machine UI preference).
3. Replace `Examples/ExampleConfigPanel.cs` with your own panel and registration.
4. Call your `Init()` from `Awake`.

The broker patches `Menu.Start` with a private Harmony instance keyed on the frozen object name, so a
second mod's copy cannot double-patch it and a plugin calling `Harmony.CreateAndPatchAll(assembly)`
cannot either.
