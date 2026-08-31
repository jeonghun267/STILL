using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 한 기기를 여러 사람이 함께 쓸 수 있는 로컬 해양생물 도감 저장소.
/// FishData 참조가 아니라 변경되지 않는 speciesId 를 저장하므로 씬/프리팹 재빌드에도 기록이 유지된다.
/// </summary>
public sealed class FishCollectionStore : MonoBehaviour
{
    [Serializable]
    public sealed class DiscoveryRecord
    {
        public string speciesId;
        public string firstDiscoveredUtc;
        public int discoveryCount;
    }

    [Serializable]
    public sealed class ExplorerProfile
    {
        public string id;
        public string displayName;
        public List<DiscoveryRecord> discoveries = new List<DiscoveryRecord>();
    }

    [Serializable]
    sealed class SaveData
    {
        public int version = 1;
        public string activeProfileId;
        public List<ExplorerProfile> profiles = new List<ExplorerProfile>();
    }

    public static FishCollectionStore Instance { get; private set; }
    public event Action Changed;

    const string SaveFileName = "marine_collection.json";
    const int DefaultProfileCount = 3;

    SaveData data;
    string savePath;

    public ExplorerProfile ActiveProfile
    {
        get
        {
            EnsureData();
            ExplorerProfile found = data.profiles.Find(p => p != null && p.id == data.activeProfileId);
            return found ?? data.profiles[0];
        }
    }

    public IReadOnlyList<ExplorerProfile> Profiles
    {
        get { EnsureData(); return data.profiles; }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("FishCollectionStore");
        DontDestroyOnLoad(go);
        go.AddComponent<FishCollectionStore>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        savePath = Path.Combine(Application.persistentDataPath, SaveFileName);
        Load();
    }

    public bool RegisterDiscovery(FishData fish)
    {
        if (fish == null) return false;
        string id = StableId(fish);
        if (string.IsNullOrWhiteSpace(id)) return false;

        ExplorerProfile profile = ActiveProfile;
        DiscoveryRecord record = profile.discoveries.Find(d => d != null && d.speciesId == id);
        bool isNew = record == null;

        if (isNew)
        {
            record = new DiscoveryRecord
            {
                speciesId = id,
                firstDiscoveredUtc = DateTime.UtcNow.ToString("O"),
                discoveryCount = 1
            };
            profile.discoveries.Add(record);
        }
        else
        {
            record.discoveryCount = Mathf.Max(1, record.discoveryCount + 1);
        }

        Save();
        Changed?.Invoke();
        Debug.Log($"[도감] {profile.displayName}: {fish.DisplayName} {(isNew ? "신규 발견" : "재발견")} ({record.discoveryCount}회)");
        return isNew;
    }

    public bool IsDiscovered(FishData fish)
    {
        if (fish == null) return false;
        string id = StableId(fish);
        return ActiveProfile.discoveries.Exists(d => d != null && d.speciesId == id);
    }

    public int GetDiscoveryCount(FishData fish)
    {
        if (fish == null) return 0;
        string id = StableId(fish);
        DiscoveryRecord record = ActiveProfile.discoveries.Find(d => d != null && d.speciesId == id);
        return record != null ? Mathf.Max(1, record.discoveryCount) : 0;
    }

    public int GetDiscoveredSpeciesCount()
    {
        return ActiveProfile.discoveries.Count;
    }

    public void SelectRelativeProfile(int delta)
    {
        EnsureData();
        int current = data.profiles.FindIndex(p => p != null && p.id == data.activeProfileId);
        if (current < 0) current = 0;
        int next = (current + delta) % data.profiles.Count;
        if (next < 0) next += data.profiles.Count;
        data.activeProfileId = data.profiles[next].id;
        Save();
        Changed?.Invoke();
    }

    public static string StableId(FishData fish)
    {
        if (fish == null) return string.Empty;
        return string.IsNullOrWhiteSpace(fish.speciesId) ? fish.name : fish.speciesId.Trim();
    }

    void EnsureData()
    {
        if (data == null) data = new SaveData();
        if (data.profiles == null) data.profiles = new List<ExplorerProfile>();

        for (int i = data.profiles.Count; i < DefaultProfileCount; i++)
        {
            int number = i + 1;
            data.profiles.Add(new ExplorerProfile
            {
                id = "explorer-" + number,
                displayName = "탐험가 " + number,
                discoveries = new List<DiscoveryRecord>()
            });
        }

        for (int i = 0; i < data.profiles.Count; i++)
        {
            ExplorerProfile profile = data.profiles[i];
            if (profile.discoveries == null) profile.discoveries = new List<DiscoveryRecord>();
        }

        if (string.IsNullOrWhiteSpace(data.activeProfileId) ||
            !data.profiles.Exists(p => p != null && p.id == data.activeProfileId))
            data.activeProfileId = data.profiles[0].id;
    }

    void Load()
    {
        try
        {
            if (File.Exists(savePath))
                data = JsonUtility.FromJson<SaveData>(File.ReadAllText(savePath));
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[도감] 저장 파일을 읽지 못해 새 도감으로 시작합니다. " + ex.Message);
            data = null;
        }

        EnsureData();
    }

    void Save()
    {
        EnsureData();
        try
        {
            string directory = Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            string temporary = savePath + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(data, true));
            if (File.Exists(savePath)) File.Delete(savePath);
            File.Move(temporary, savePath);
        }
        catch (Exception ex)
        {
            Debug.LogError("[도감] 저장에 실패했습니다. " + ex.Message);
        }
    }
}
