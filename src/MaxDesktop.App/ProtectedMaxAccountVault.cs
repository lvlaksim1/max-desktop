using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MaxDesktop.Protocol;

namespace MaxDesktop.App;

/// <summary>
/// Per-Windows-user DPAPI vault containing independent MAX sessions.
/// Keeps the old single-account file untouched for rollback to v0.1.16.
/// </summary>
public sealed class ProtectedMaxAccountVault
{
    private readonly string _directory;
    private readonly Func<MaxSavedSession?> _legacyLoader;
    private readonly object _gate = new();
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MaxDesktop.AccountVault.v1");
    private VaultDocument? _snapshot;

    public ProtectedMaxAccountVault(string? directory = null,
        Func<MaxSavedSession?>? legacyLoader = null)
    {
        _directory = directory ?? SettingsStore.DataRoot;
        _legacyLoader = legacyLoader ?? (() => new ProtectedMaxSessionStore().Load());
    }

    private string PathName => Path.Combine(_directory, "max-accounts.dat");

    private sealed class VaultDocument
    {
        public VaultDocument() { }
        public int FormatVersion { get; set; } = 1;
        public bool LegacyImported { get; set; }
        public string? ActivePhone { get; set; }
        public List<MaxSavedSession> Accounts { get; set; } = new();
    }

    private VaultDocument Get()
    {
        if (_snapshot is not null) return _snapshot;
        VaultDocument document;
        if (File.Exists(PathName))
        {
            byte[] encrypted = File.ReadAllBytes(PathName);
            byte[] bytes = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            try
            {
                document = JsonSerializer.Deserialize<VaultDocument>(bytes)
                    ?? throw new InvalidDataException("Хранилище аккаунтов MAX пусто.");
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
            if (document.FormatVersion != 1 || document.Accounts is null)
                throw new InvalidDataException("Неподдерживаемая версия хранилища аккаунтов.");
            foreach (var session in document.Accounts)
                Validate(session);
            if (document.Accounts.GroupBy(x => x.Phone, StringComparer.Ordinal).Any(g => g.Count() != 1))
                throw new InvalidDataException("В хранилище MAX дублируются номера аккаунтов.");
        }
        else
            document = new VaultDocument();

        // Import once; no destructive migration of the old DPAPI file.
        if (!document.LegacyImported)
        {
            MaxSavedSession? legacy = _legacyLoader();
            if (legacy is not null && !string.IsNullOrWhiteSpace(legacy.LoginToken))
            {
                Validate(legacy);
                if (!document.Accounts.Any(s => s.Phone == legacy.Phone))
                    document.Accounts.Add(legacy);
                document.ActivePhone ??= legacy.Phone;
            }
            document.LegacyImported = true;
            Save(document);
        }
        _snapshot = document;
        return document;
    }

    public IReadOnlyList<string> Phones()
    {
        lock (_gate) return Get().Accounts.Select(a => a.Phone).ToArray();
    }

    public string? ActivePhone
    {
        get { lock (_gate) return Get().ActivePhone; }
    }

    public MaxSavedSession? Find(string phone)
    {
        lock (_gate) return Get().Accounts.FirstOrDefault(a => a.Phone == phone);
    }

    public void Upsert(MaxSavedSession session)
    {
        Validate(session);
        lock (_gate)
        {
            VaultDocument state = Get();
            var next = Clone(state);
            int index = next.Accounts.FindIndex(a => a.Phone == session.Phone);
            if (index >= 0) next.Accounts[index] = session;
            else next.Accounts.Add(session);
            next.ActivePhone ??= session.Phone;
            Save(next);
            _snapshot = next;
        }
    }

    public void Activate(string phone)
    {
        lock (_gate)
        {
            VaultDocument state = Get();
            if (!state.Accounts.Any(x => x.Phone == phone))
                throw new InvalidOperationException("Указанный аккаунт MAX не сохранён.");
            if (state.ActivePhone == phone) return;
            var next = Clone(state);
            next.ActivePhone = phone;
            Save(next);
            _snapshot = next;
        }
    }

    public void Remove(string phone)
    {
        lock (_gate)
        {
            VaultDocument state = Get();
            if (!state.Accounts.Any(x => x.Phone == phone)) return;
            var next = Clone(state);
            next.Accounts.RemoveAll(x => x.Phone == phone);
            if (next.ActivePhone == phone)
                next.ActivePhone = next.Accounts.FirstOrDefault()?.Phone;
            Save(next);
            _snapshot = next;
        }
    }

    public IMaxSessionStore Slot(string? phone = null) => new AccountSlot(this, phone);

    private sealed class AccountSlot(ProtectedMaxAccountVault vault, string? slotPhone) : IMaxSessionStore
    {
        private string? _savedPhone;
        public MaxSavedSession? Load() => slotPhone is null ? null : vault.Find(slotPhone);
        public void Save(MaxSavedSession session)
        {
            if (slotPhone is not null && slotPhone != session.Phone)
                throw new InvalidOperationException("Нельзя записать другой аккаунт в чужой сеанс.");
            vault.Upsert(session);
            _savedPhone = session.Phone;
        }
        public void Delete()
        {
            string? phone = slotPhone ?? _savedPhone;
            if (phone is not null) vault.Remove(phone);
        }
    }

    private static VaultDocument Clone(VaultDocument document) => new()
    {
        FormatVersion = document.FormatVersion,
        LegacyImported = document.LegacyImported,
        ActivePhone = document.ActivePhone,
        Accounts = document.Accounts.ToList()
    };

    private static void Validate(MaxSavedSession session)
    {
        if (string.IsNullOrWhiteSpace(session.Phone) || !session.Phone.StartsWith('+') ||
            string.IsNullOrWhiteSpace(session.LoginToken) ||
            string.IsNullOrWhiteSpace(session.DeviceId) ||
            string.IsNullOrWhiteSpace(session.InstanceId))
            throw new InvalidDataException("Неполные сведения сохранённого аккаунта MAX.");
    }

    private void Save(VaultDocument document)
    {
        Directory.CreateDirectory(_directory);
        byte[] raw = JsonSerializer.SerializeToUtf8Bytes(document);
        try
        {
            byte[] encrypted = ProtectedData.Protect(raw, Entropy, DataProtectionScope.CurrentUser);
            string temp = PathName + ".tmp";
            try
            {
                File.WriteAllBytes(temp, encrypted);
                File.Move(temp, PathName, true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        finally { CryptographicOperations.ZeroMemory(raw); }
    }
}
