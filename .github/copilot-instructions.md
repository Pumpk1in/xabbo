# Xabbo Copilot Instructions

## Project Overview
Xabbo is a cross-platform G-Earth extension for Habbo game clients (Flash & Origins) built with .NET 8. It uses **Avalonia** for UI, **ReactiveUI** for MVVM reactivity, and modular architecture with layered dependencies: libcommon → libcore/libmessages → Xabbo (business logic) → Xabbo.Avalonia (UI).

## Architecture & Data Flow

### Layer Structure
- **Xabbo.Common** (lib/common): Primitives, parsers/composers, message interfaces
- **Xabbo.Messages** (lib/messages): Protocol message definitions  
- **Xabbo.GEarth** (lib/gearth): G-Earth integration, extension lifecycle
- **Xabbo** (src/): Business logic - controllers, state managers (RoomManager, ProfileManager), services
- **Xabbo.Avalonia** (src/): UI layer - views, viewmodels, behaviors using Avalonia/FluentUI

### Critical Data Flow Patterns
1. **Message Interception**: Extension receives packets → IMessageHandler.Attach() → Intercept<T>()
2. **Game State**: Managers (RoomManager, ProfileManager) track game state via interceptors; changes trigger events (e.g., AvatarChat, AvatarsAdded)
3. **Controllers**: Extend ControllerBase, inherit IExtension + Session access, auto-attach on Connected
4. **ViewModels**: Extend ViewModelBase (ReactiveObject) or ControllerBase (for game state managers)

**Key Files**: [Xabbo.Core](lib/core/src/Xabbo.Core) (game managers), [Controllers](src/Xabbo/Controllers) (request handlers), [ViewModels](src/Xabbo/ViewModels) (UI logic)

## Commands & Modules

### Command Pattern
- Commands use `/` prefix, defined via `[Command]` attribute on methods in `CommandModule` subclasses
- CommandManager auto-discovers modules, validates permissions via Xabbot component
- Example: `[Command("kick {username}")]` → parsed by CommandBinding with type coercion

**Reference**: [CommandManager.cs](src/Xabbo/Command/CommandManager.cs), [Modules](src/Xabbo/Command/Modules)

## UI & Bindings

### Avalonia + ReactiveUI Stack
- **Compiled bindings** enabled (check XAML x:DataType)
- **Behaviors** for reusable logic: [ScrollToBottom](src/Xabbo.Avalonia/Behaviors), [AutoScroll](src/Xabbo.Avalonia/Behaviors)
- **Resources**: FluentUI (FAMenuFlyout, MenuFlyoutItem), FluentIcons (SymbolIcon), custom controls in [Controls/](src/Xabbo.Avalonia/Controls)
- **Converters**: DynamicResource for theming, StringConverters built-in (IsNotNullOrEmpty, etc.)

**Pattern Example** (ChatPage.axaml): ListBox with reactive filtering, command flyouts, custom ItemTemplate data flow
- Filter TextBox → FilterText binding → ViewModel observable
- Button commands bound to `{Binding ExportChatCmd}`
- MultiBinding converters for complex visibility logic

## Development Workflows

### Build & Run
```bash
# Restore submodules (one-time)
git submodule update --init

# Run dev server
dotnet run --project src/Xabbo.Avalonia

# Build release
dotnet build -c Release
```

**Taskfile shortcuts**:
- `task run` - build & run Avalonia app
- `task pack` - package extension
- `task new-view` / `task new-control` - scaffolding (interactive)

### Code Standards
- **Nullability**: `#nullable enable` enabled globally; use `IExtension? ext` patterns
- **ReactiveUI conventions**: Use `[Reactive]` attributes, `WhenAnyValue()` for observables
- **Code style**: xStyler formatting (run via task or pre-commit)
- **Namespaces**: Match folder structure (e.g., `src/Xabbo/Controllers/RoomModerationController.cs` → `namespace Xabbo.Controllers`)

## Key Patterns & Conventions

### Services Pattern
- `IExtension` provides Session, Dispatcher, Messages
- Services registered in DI container (Splat, Microsoft.Extensions.DependencyInjection)
- Stateless operations should be static or extension methods on common types (e.g., UserData)

### Game State Tracking
- RoomManager, ProfileManager intercept packets to maintain observable game state
- Events expose deltas (e.g., `AvatarsAdded` with Avatar[], not full state)
- Use `.Where()` LINQ chains to filter state before subscribing (performance)

### Configuration
- AppConfig (appsettings.json) loaded via IConfiguration binder
- Runtime configs stored in SqliteDatabase via ConfigProvider
- Design-time data for XAML designer in Design/

## Chat System Architecture

### Live Chat & ChatLog
**Components**: [ChatPageViewModel](src/Xabbo/ViewModels/Chat/ChatPageViewModel.cs), [ChatMessageViewModel](src/Xabbo/ViewModels/Chat/ChatMessageViewModel.cs), [ChatHistoryService](src/Xabbo/Services/ChatHistoryService.cs)

**Data Flow**:
1. RoomManager intercepts AvatarChat events → ChatPageViewModel processes
2. ChatMessageViewModel created with MessageSegments (for profanity highlighting)
3. Entry saved to SqliteDatabase via ChatHistoryService.AddEntry()
4. SourceCache maintains in-memory collection with filtering (FilterText, WhispersOnly, ProfanityOnly)

**Key Properties**:
- `Messages`: ReadOnlyObservableCollection filtered by FilterText regex
- `HasProfanity`: bool flag on ChatMessageViewModel
- `MatchedWords`: IReadOnlyList<string> from profanity filter
- `Selection`: MultiSelect for bulk operations

### Profanity Filter System
**Service**: [ProfanityFilterService](src/Xabbo/Services/ProfanityFilterService.cs)

**Features**:
- **Obfuscation Detection**: Regex patterns for common substitutions (conne → c0nne, cxnne, etc.)
- **Character Pattern Map**: `CharacterPatterns` dictionary (a→[a@4àáâ], e→[e3€èéê], etc.)
- **Filler Characters**: `[x*_\-.\\s]*` between letters to catch variations
- **Custom Words**: Observable collection in AppConfig triggers RebuildPatterns()

**Key Methods**:
```csharp
// Detection
bool ContainsProfanity(string msg);
IReadOnlyList<ProfanityMatch> FindMatches(string msg);  // Returns (Start, Length, MatchedWord)

// Management
void AddWord(string word);
void RemoveWord(string word);
```

**Integration**: ChatPageViewModel calls `_profanityFilter.FindMatches()` → builds MessageSegments with IsProfanity flags

### Chat History Search
**Service**: [ChatHistoryService](src/Xabbo/Services/ChatHistoryService.cs) (SQLite backend)

**Database Schema**:
```sql
CREATE TABLE chat_history (
  id, timestamp, type ("message"/"action"/"room"),
  name, message, chat_type, is_whisper, has_profanity,
  matched_words (JSON array), user_name, action,
  room_name, room_owner
);
-- Indexes: timestamp, name, user_name, has_profanity
```

**Query Methods**:
```csharp
Search(userName?, keyword?, profanityOnly?, fromDate?, toDate?, limit?)
SearchWithCount(...)  // Returns (Results, TotalCount)
```

**Usage in ChatPageViewModel**:
- HistorySearchUser, HistorySearchKeyword, HistorySearchProfanityOnly (reactive properties)
- SearchHistoryCmd → executes search with date range
- HistoryResults collection populated with ChatHistoryEntry objects
- HighlightedTextBlock shows matched keywords

### Moderation Actions
**Controller**: [RoomModerationController](src/Xabbo/Controllers/RoomModerationController.cs)

**Permission Model**:
```csharp
enum ModerationType { Mute, Unmute, Kick, Ban, Unban, Bounce }

bool CanModerate(ModerationType type, IUser user) =>
  // User.RightsLevel > Target.RightsLevel && !Target.IsStaff
  type switch
  {
    Mute/Unmute => CanMute && ...,
    Kick => CanKick && ...,
    Ban => CanBan && ...,
    Unban => RightsLevel >= GroupAdmin,
    Bounce => CanBan && ...  // Ban + delayed unban
  }
```

**Commands in ChatPageViewModel**:
- `MuteUsersCmd`: (string Minutes) → MuteUsersAsync(selectedUsers, int.Parse(param))
- `KickUsersCmd`: no param → KickUsersAsync(selectedUsers)
- `BanUsersCmd`: (BanDuration) → BanUsersAsync(selectedUsers, param)
- `KickUserByNameCmd`, `MuteUserByNameCmd`, `BanUserByNameCmd`: Quick actions on profanity messages

**Quick Moderation UI** (ChatPage.axaml):
- Buttons shown only on HasProfanity messages via ShowModerationButtons property
- Hidden during fast scroll (ScrollThrottleBehavior.IsThrottled)
- Pre-bound parameters: MuteParam2/5/10/15/30/60, BanParamHour/Day/Permanent

## Common Tasks

### Adding a New Feature
1. Create ViewModel in [ViewModels/](src/Xabbo/ViewModels) extending appropriate base
2. Create View (XAML) + CodeBehind in [Views/](src/Xabbo.Avalonia/Views)
3. Wire into MainViewModel navigation/pages dictionary
4. Register in DI (Program.cs ServiceLocator)

### Intercepting Messages
```csharp
ext.Intercept<ChatMsg>(chat => Console.WriteLine(chat.Message));
ext.Intercept<UserDataMsg>((e, msg) => {
    if (msg.Id == myId) e.Block(); // block packet
});
```

### Binding Game State to UI
- ViewModels inherit ControllerBase for Session/Extension access
- Use WhenAnyValue(vm => vm.SelectedUser).Subscribe(...) for reactive updates
- Managers (RoomManager) emit events; convert to observables via Subject<T>

## Testing Considerations
- Test projects use xUnit (*.Tests.csproj naming)
- Generators for code-gen validation ([Xabbo.Common.Generator](lib/common/src/Xabbo.Common.Generator))
- Mock IExtension via Moq or stubs for message interception tests

## Git & Submodules
Repository uses submodules for lib/* (common, core, messages, gearth). After clone, run:
```bash
git submodule update --init
```
Changes to submodules require separate PRs in their repos.
