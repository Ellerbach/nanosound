using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using Iot.Device.Yx5300;

namespace nanoSound
{
    public sealed class SoundTrack
    {
        public int Number;
        public string Name;
        public string Schedule;
        public long LastPlayedMinuteTicks = -1;
    }

    public sealed class SoundGroup
    {
        public int Number;
        public string Name;
        public bool RandomEnabled;
        public int MinimumDelaySeconds = 60;
        public int MaximumDelaySeconds = 300;
        public DateTime NextRandomTimeUtc = DateTime.MinValue;
        public SoundTrack[] Tracks;
    }

    public sealed class SoundCatalog
    {
        private const string ConfigurationPath = "I:\\sound-config.txt";
        private const string TemporaryPath = "I:\\sound-config.tmp";

        private readonly object _syncRoot = new object();
        private SoundGroup[] _groups = new SoundGroup[0];
        private int _volume = 15;
        private int _timeZoneOffsetMinutes;
        private string _accessPointName = AccessPointService.Ssid;
        private string _accessPointPassword = AccessPointService.Password;
        private int _revision;
        private int _pendingFolder;
        private int _pendingTrack;

        public int Volume
        {
            get
            {
                lock (_syncRoot)
                {
                    return _volume;
                }
            }
        }

        public int Revision
        {
            get
            {
                lock (_syncRoot)
                {
                    return _revision;
                }
            }
        }

        public int TimeZoneOffsetMinutes
        {
            get
            {
                lock (_syncRoot)
                {
                    return _timeZoneOffsetMinutes;
                }
            }
        }

        public string AccessPointName
        {
            get
            {
                lock (_syncRoot)
                {
                    return _accessPointName;
                }
            }
        }

        public string AccessPointPassword
        {
            get
            {
                lock (_syncRoot)
                {
                    return _accessPointPassword;
                }
            }
        }

        public void Initialize(Yx5300 player)
        {
            Load();

            if (player == null)
            {
                Debug.WriteLine("Sound discovery unavailable; using saved catalog.");
                return;
            }

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    int[] trackCounts = DiscoverTrackCounts(player);
                    Reconcile(trackCounts);
                    Debug.WriteLine("Discovered " + trackCounts.Length.ToString() + " sound folders from the MP3 module.");
                    return;
                }
                catch (Exception exception)
                {
                    Debug.WriteLine("Sound discovery attempt " + attempt.ToString() + " failed: " + exception.Message);
                    Thread.Sleep(250);
                }
            }

            Debug.WriteLine("Sound discovery failed; using saved catalog.");
        }

        private static int[] DiscoverTrackCounts(Yx5300 player)
        {
            int originalVolume = player.GetVolume();
            Debug.WriteLine("Volume: " + originalVolume.ToString());
            int folderCount = player.GetFolderCount();
            Debug.WriteLine("Folders: " + folderCount.ToString());
            if (folderCount <= 0 || folderCount > 99)
            {
                throw new InvalidOperationException("The MP3 module returned an invalid folder count.");
            }

            int totalTrackCount = player.GetTotalFileCount();
            Debug.WriteLine("Files on card: " + totalTrackCount.ToString());
            int discoveredTrackCount = 0;
            int[] trackCounts = new int[folderCount];

            player.Volume(0);
            EnsureCommandAccepted(player, nameof(Yx5300.Volume));
            Thread.Sleep(100);

            try
            {
                for (int folder = 1; folder <= folderCount; folder++)
                {
                    player.PlaySpecific(folder, 1);
                    EnsureCommandAccepted(player, "PlaySpecific(folder " + folder.ToString() + ", file 1)");
                    Thread.Sleep(500);

                    int trackCount = player.GetFolderFileCount(folder);
                    Debug.WriteLine("Files in folder " + folder.ToString() + ": " + trackCount.ToString());
                    if (trackCount <= 0)
                    {
                        throw new InvalidOperationException("The MP3 module returned an empty folder.");
                    }

                    trackCounts[folder - 1] = trackCount;
                    discoveredTrackCount += trackCount;
                }
            }
            finally
            {
                try
                {
                    Thread.Sleep(100);
                    player.Stop();
                    EnsureCommandAccepted(player, nameof(Yx5300.Stop));
                }
                finally
                {
                    Thread.Sleep(100);
                    player.Volume(originalVolume);
                    EnsureCommandAccepted(player, "Volume(restore)");
                    Thread.Sleep(100);
                }
            }

            if (discoveredTrackCount != totalTrackCount)
            {
                throw new InvalidOperationException("Folder counts do not match the MP3 module's total track count.");
            }

            return trackCounts;
        }

        private static void EnsureCommandAccepted(Yx5300 player, string command)
        {
            if (player.GetStatusCode() != Yx5300.StatusCode.AcknowledgeOk)
            {
                string message = command + " failed: status " + player.GetStatusCode().ToString() + ", data " + player.GetStatusData().ToString() + ".";
                Debug.WriteLine(message);
                throw new InvalidOperationException(message);
            }
        }

        public SoundGroup[] GetGroups()
        {
            lock (_syncRoot)
            {
                SoundGroup[] copy = new SoundGroup[_groups.Length];
                for (int index = 0; index < _groups.Length; index++)
                {
                    copy[index] = CopyGroup(_groups[index]);
                }

                return copy;
            }
        }

        public string ExportConfiguration()
        {
            lock (_syncRoot)
            {
                return BuildConfigurationLocked();
            }
        }

        public void ImportConfiguration(string configuration)
        {
            if (string.IsNullOrEmpty(configuration) || configuration.Length > 65535)
            {
                throw new ArgumentException("Invalid configuration file.");
            }

            SoundCatalog imported = new SoundCatalog();
            imported.ParseConfiguration(configuration, true);

            lock (_syncRoot)
            {
                int[] discoveredTrackCounts = new int[_groups.Length];
                for (int index = 0; index < _groups.Length; index++)
                {
                    discoveredTrackCounts[index] = _groups[index].Tracks.Length;
                }

                _volume = imported._volume;
                _timeZoneOffsetMinutes = imported._timeZoneOffsetMinutes;
                _accessPointName = imported._accessPointName;
                _accessPointPassword = imported._accessPointPassword;
                _groups = imported._groups;
                if (discoveredTrackCounts.Length > 0)
                {
                    ReconcileLocked(discoveredTrackCounts);
                }

                _revision++;
                SaveLocked();
            }
        }

        public void UpdateVolume(int volume)
        {
            if (volume < 0 || volume > 30)
            {
                throw new ArgumentOutOfRangeException();
            }

            lock (_syncRoot)
            {
                _volume = volume;
                _revision++;
                SaveLocked();
            }
        }

        public void UpdateGroup(int folder, string name, bool randomEnabled, int minimumDelaySeconds, int maximumDelaySeconds)
        {
            if (string.IsNullOrEmpty(name) || minimumDelaySeconds < 0 || maximumDelaySeconds < minimumDelaySeconds || maximumDelaySeconds > 86400)
            {
                throw new ArgumentOutOfRangeException();
            }

            lock (_syncRoot)
            {
                SoundGroup group = FindGroup(folder);
                if (group == null)
                {
                    throw new ArgumentOutOfRangeException();
                }

                group.Name = name;
                group.RandomEnabled = randomEnabled;
                group.MinimumDelaySeconds = minimumDelaySeconds;
                group.MaximumDelaySeconds = maximumDelaySeconds;
                group.NextRandomTimeUtc = DateTime.MinValue;
                _revision++;
                SaveLocked();
            }
        }

        public void UpdateTrack(int folder, int track, string name, string schedule)
        {
            if (string.IsNullOrEmpty(name) || !IsValidSchedule(schedule))
            {
                throw new ArgumentException();
            }

            lock (_syncRoot)
            {
                SoundTrack soundTrack = FindTrack(folder, track);
                if (soundTrack == null)
                {
                    throw new ArgumentOutOfRangeException();
                }

                soundTrack.Name = name;
                soundTrack.Schedule = schedule == null ? string.Empty : schedule;
                soundTrack.LastPlayedMinuteTicks = -1;
                _revision++;
                SaveLocked();
            }
        }

        public void SetTimeZoneOffsetMinutes(int value)
        {
            if (value < -840 || value > 840)
            {
                throw new ArgumentOutOfRangeException();
            }

            lock (_syncRoot)
            {
                _timeZoneOffsetMinutes = value;
                _revision++;
                SaveLocked();
            }
        }

        public void UpdateAccessPoint(string name, string password)
        {
            int nameLength = string.IsNullOrEmpty(name) ? 0 : Encoding.UTF8.GetBytes(name).Length;
            int passwordLength = password == null ? 0 : Encoding.UTF8.GetBytes(password).Length;
            if (nameLength < 1 || nameLength > 32 || passwordLength < 8 || passwordLength > 63)
            {
                throw new ArgumentOutOfRangeException();
            }

            lock (_syncRoot)
            {
                _accessPointName = name;
                _accessPointPassword = password;
                _revision++;
                SaveLocked();
            }
        }

        public void Reset()
        {
            lock (_syncRoot)
            {
                int[] trackCounts = new int[_groups.Length];
                for (int index = 0; index < _groups.Length; index++)
                {
                    trackCounts[index] = _groups[index].Tracks.Length;
                }

                _groups = new SoundGroup[0];
                _volume = 15;
                _timeZoneOffsetMinutes = 0;
                _accessPointName = AccessPointService.Ssid;
                _accessPointPassword = AccessPointService.Password;
                ReconcileLocked(trackCounts);
                _revision++;
                SaveLocked();
            }
        }

        public void QueueTrack(int folder, int track)
        {
            lock (_syncRoot)
            {
                if (FindTrack(folder, track) == null)
                {
                    throw new ArgumentOutOfRangeException();
                }

                _pendingFolder = folder;
                _pendingTrack = track;
            }
        }

        public bool TryTakeTrack(out int folder, out int track)
        {
            lock (_syncRoot)
            {
                folder = _pendingFolder;
                track = _pendingTrack;
                _pendingFolder = 0;
                _pendingTrack = 0;
                return folder != 0;
            }
        }

        public bool TryGetScheduledTrack(DateTime utcNow, Random random, out int folder, out int track)
        {
            lock (_syncRoot)
            {
                DateTime localNow = utcNow.AddMinutes(-_timeZoneOffsetMinutes);
                DateTime minute = new DateTime(localNow.Year, localNow.Month, localNow.Day, localNow.Hour, localNow.Minute, 0);

                foreach (SoundGroup group in _groups)
                {
                    foreach (SoundTrack soundTrack in group.Tracks)
                    {
                        if (soundTrack.LastPlayedMinuteTicks != minute.Ticks && MatchesSchedule(soundTrack.Schedule, localNow))
                        {
                            soundTrack.LastPlayedMinuteTicks = minute.Ticks;
                            folder = group.Number;
                            track = soundTrack.Number;
                            return true;
                        }
                    }
                }

                foreach (SoundGroup group in _groups)
                {
                    if (!group.RandomEnabled || group.Tracks.Length == 0)
                    {
                        continue;
                    }

                    if (group.NextRandomTimeUtc == DateTime.MinValue)
                    {
                        group.NextRandomTimeUtc = utcNow.AddSeconds(GetRandomDelay(group, random));
                    }
                    else if (utcNow >= group.NextRandomTimeUtc)
                    {
                        group.NextRandomTimeUtc = utcNow.AddSeconds(GetRandomDelay(group, random));
                        folder = group.Number;
                        track = random.Next(group.Tracks.Length) + 1;
                        return true;
                    }
                }

                folder = 0;
                track = 0;
                return false;
            }
        }

        private void Reconcile(int[] trackCounts)
        {
            lock (_syncRoot)
            {
                ReconcileLocked(trackCounts);
                SaveLocked();
            }
        }

        private void ReconcileLocked(int[] trackCounts)
        {
            SoundGroup[] reconciled = new SoundGroup[trackCounts.Length];
            for (int folderIndex = 0; folderIndex < trackCounts.Length; folderIndex++)
            {
                int folder = folderIndex + 1;
                SoundGroup existing = FindGroup(folder);
                SoundGroup group = existing ?? CreateDefaultGroup(folder);
                SoundTrack[] tracks = new SoundTrack[trackCounts[folderIndex]];
                for (int trackIndex = 0; trackIndex < tracks.Length; trackIndex++)
                {
                    int track = trackIndex + 1;
                    SoundTrack existingTrack = existing == null ? null : FindTrack(existing, track);
                    tracks[trackIndex] = existingTrack ?? CreateDefaultTrack(folder, track);
                }

                group.Tracks = tracks;
                reconciled[folderIndex] = group;
            }

            _groups = reconciled;
        }

        private void Load()
        {
            if (!File.Exists(ConfigurationPath))
            {
                return;
            }

            try
            {
                ParseConfiguration(File.ReadAllText(ConfigurationPath), false);
            }
            catch (Exception exception)
            {
                Debug.WriteLine("Saved sound configuration is invalid: " + exception.Message);
                _groups = new SoundGroup[0];
                _volume = 15;
                _timeZoneOffsetMinutes = 0;
                _accessPointName = AccessPointService.Ssid;
                _accessPointPassword = AccessPointService.Password;
            }
        }

        private void ParseConfiguration(string configuration, bool rejectInvalidRecords)
        {
            string[] lines = configuration.Split('\n');
            ArrayList groups = new ArrayList();
            int invalidRecords = 0;
            bool versionFound = false;
            bool settingsFound = false;
            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                string[] parts = line.Split('|');
                if (parts[0] == "V")
                {
                    if (!versionFound && parts.Length == 2 && parts[1] == "2")
                    {
                        versionFound = true;
                    }
                    else
                    {
                        invalidRecords++;
                    }
                }
                else if (parts[0] == "S")
                {
                    if (!settingsFound && parts.Length == 5 &&
                        int.TryParse(parts[1], out int volume) && volume >= 0 && volume <= 30 &&
                        int.TryParse(parts[2], out int offset) && offset >= -840 && offset <= 840 &&
                        TryDecode(parts[3], out string accessPointName) &&
                        TryDecode(parts[4], out string accessPointPassword) &&
                        Encoding.UTF8.GetBytes(accessPointName).Length >= 1 && Encoding.UTF8.GetBytes(accessPointName).Length <= 32 &&
                        Encoding.UTF8.GetBytes(accessPointPassword).Length >= 8 && Encoding.UTF8.GetBytes(accessPointPassword).Length <= 63)
                    {
                        _volume = volume;
                        _timeZoneOffsetMinutes = offset;
                        _accessPointName = accessPointName;
                        _accessPointPassword = accessPointPassword;
                        settingsFound = true;
                    }
                    else
                    {
                        invalidRecords++;
                    }
                }
                else if (parts[0] == "G")
                {
                    if (parts.Length == 7 &&
                        int.TryParse(parts[1], out int folder) && folder > 0 && folder <= 99 &&
                        TryDecode(parts[2], out string groupName) && !string.IsNullOrEmpty(groupName) &&
                        (parts[3] == "0" || parts[3] == "1") &&
                        int.TryParse(parts[4], out int minimum) && minimum >= 0 &&
                        int.TryParse(parts[5], out int maximum) && maximum >= minimum && maximum <= 86400 &&
                        int.TryParse(parts[6], out int trackCount) && trackCount >= 0 && trackCount <= 255 &&
                        FindGroup(groups, folder) == null)
                    {
                        groups.Add(new SoundGroup
                        {
                            Number = folder,
                            Name = groupName,
                            RandomEnabled = parts[3] == "1",
                            MinimumDelaySeconds = minimum,
                            MaximumDelaySeconds = maximum,
                            Tracks = new SoundTrack[trackCount]
                        });
                    }
                    else
                    {
                        invalidRecords++;
                    }
                }
                else if (parts[0] == "T")
                {
                    if (parts.Length == 5 &&
                        int.TryParse(parts[1], out int folder) &&
                        int.TryParse(parts[2], out int track) &&
                        TryDecode(parts[3], out string trackName) && !string.IsNullOrEmpty(trackName) &&
                        TryDecode(parts[4], out string schedule) && IsValidSchedule(schedule))
                    {
                        SoundGroup group = FindGroup(groups, folder);
                        if (group != null && track >= 1 && track <= group.Tracks.Length && group.Tracks[track - 1] == null)
                        {
                            group.Tracks[track - 1] = new SoundTrack
                            {
                                Number = track,
                                Name = trackName,
                                Schedule = schedule
                            };
                            continue;
                        }
                    }

                    invalidRecords++;
                }
                else
                {
                    invalidRecords++;
                }
            }

            if (!versionFound) invalidRecords++;
            if (!settingsFound) invalidRecords++;
            _groups = new SoundGroup[groups.Count];
            groups.CopyTo(_groups);
            foreach (SoundGroup group in _groups)
            {
                foreach (SoundTrack track in group.Tracks)
                {
                    if (track == null) invalidRecords++;
                }
            }

            if (rejectInvalidRecords && invalidRecords > 0)
            {
                throw new ArgumentException("Invalid or incomplete configuration file.");
            }

            FillMissingTracks();
            if (invalidRecords > 0)
            {
                Debug.WriteLine("Ignored " + invalidRecords.ToString() + " invalid saved configuration records.");
            }
        }

        private void SaveLocked()
        {
            string configuration = BuildConfigurationLocked();
            File.WriteAllText(TemporaryPath, configuration);
            File.Copy(TemporaryPath, ConfigurationPath, true);
            File.Delete(TemporaryPath);
        }

        private string BuildConfigurationLocked()
        {
            StringBuilder output = new StringBuilder();
            output.Append("V|2\nS|").Append(_volume.ToString()).Append('|').Append(_timeZoneOffsetMinutes.ToString()).Append('|')
                .Append(Encode(_accessPointName)).Append('|').Append(Encode(_accessPointPassword)).Append('\n');
            foreach (SoundGroup group in _groups)
            {
                output.Append("G|").Append(group.Number.ToString()).Append('|').Append(Encode(group.Name)).Append('|')
                    .Append(group.RandomEnabled ? "1" : "0").Append('|').Append(group.MinimumDelaySeconds.ToString()).Append('|')
                    .Append(group.MaximumDelaySeconds.ToString()).Append('|').Append(group.Tracks.Length.ToString()).Append('\n');
                foreach (SoundTrack track in group.Tracks)
                {
                    output.Append("T|").Append(group.Number.ToString()).Append('|').Append(track.Number.ToString()).Append('|')
                        .Append(Encode(track.Name)).Append('|').Append(Encode(track.Schedule)).Append('\n');
                }
            }

            return output.ToString();
        }

        private void FillMissingTracks()
        {
            foreach (SoundGroup group in _groups)
            {
                for (int index = 0; index < group.Tracks.Length; index++)
                {
                    if (group.Tracks[index] == null)
                    {
                        group.Tracks[index] = CreateDefaultTrack(group.Number, index + 1);
                    }
                }
            }
        }

        private SoundGroup FindGroup(int folder)
        {
            foreach (SoundGroup group in _groups)
            {
                if (group.Number == folder)
                {
                    return group;
                }
            }

            return null;
        }

        private SoundTrack FindTrack(int folder, int track)
        {
            SoundGroup group = FindGroup(folder);
            return group == null ? null : FindTrack(group, track);
        }

        private static SoundTrack FindTrack(SoundGroup group, int track)
        {
            return track >= 1 && track <= group.Tracks.Length ? group.Tracks[track - 1] : null;
        }

        private static SoundGroup FindGroup(ArrayList groups, int folder)
        {
            foreach (SoundGroup group in groups)
            {
                if (group.Number == folder)
                {
                    return group;
                }
            }

            return null;
        }

        private static SoundGroup CopyGroup(SoundGroup source)
        {
            SoundGroup copy = new SoundGroup
            {
                Number = source.Number,
                Name = source.Name,
                RandomEnabled = source.RandomEnabled,
                MinimumDelaySeconds = source.MinimumDelaySeconds,
                MaximumDelaySeconds = source.MaximumDelaySeconds,
                Tracks = new SoundTrack[source.Tracks.Length]
            };
            for (int index = 0; index < source.Tracks.Length; index++)
            {
                copy.Tracks[index] = new SoundTrack
                {
                    Number = source.Tracks[index].Number,
                    Name = source.Tracks[index].Name,
                    Schedule = source.Tracks[index].Schedule
                };
            }

            return copy;
        }

        private static SoundGroup CreateDefaultGroup(int folder)
        {
            return new SoundGroup
            {
                Number = folder,
                Name = "Group " + folder.ToString(),
                RandomEnabled = false
            };
        }

        private static SoundTrack CreateDefaultTrack(int folder, int track)
        {
            return new SoundTrack
            {
                Number = track,
                Name = "Sound " + track.ToString(),
                Schedule = string.Empty
            };
        }

        private static int GetRandomDelay(SoundGroup group, Random random)
        {
            return random.Next(group.MaximumDelaySeconds - group.MinimumDelaySeconds + 1) + group.MinimumDelaySeconds;
        }

        private static bool IsValidSchedule(string schedule)
        {
            if (string.IsNullOrEmpty(schedule)) return true;
            string[] values = schedule.Split(',');
            foreach (string value in values)
            {
                string[] parts = value.Trim().Split(':');
                if (parts.Length != 2 || !int.TryParse(parts[1], out int minute) || minute < 0 || minute > 59)
                {
                    return false;
                }

                if (parts[0] != "*" && (!int.TryParse(parts[0], out int hour) || hour < 0 || hour > 23))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool MatchesSchedule(string schedule, DateTime localNow)
        {
            if (string.IsNullOrEmpty(schedule)) return false;
            string[] values = schedule.Split(',');
            foreach (string value in values)
            {
                string[] parts = value.Trim().Split(':');
                if (parts.Length == 2 && int.TryParse(parts[1], out int minute) && minute == localNow.Minute &&
                    (parts[0] == "*" || (int.TryParse(parts[0], out int hour) && hour == localNow.Hour)))
                {
                    return true;
                }
            }

            return false;
        }

        private static string Encode(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        }

        private static bool TryDecode(string value, out string decoded)
        {
            decoded = null;
            if (value == null || value.Length % 4 != 0)
            {
                return false;
            }

            if (value.Length == 0)
            {
                decoded = string.Empty;
                return true;
            }

            int padding = 0;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (character == '=')
                {
                    if (index < value.Length - 2 || ++padding > 2)
                    {
                        return false;
                    }
                }
                else
                {
                    bool valid = (character >= 'A' && character <= 'Z') ||
                        (character >= 'a' && character <= 'z') ||
                        (character >= '0' && character <= '9') ||
                        character == '+' || character == '/';
                    if (!valid || padding != 0)
                    {
                        return false;
                    }
                }
            }

            byte[] bytes = Convert.FromBase64String(value);
            decoded = Encoding.UTF8.GetString(bytes, 0, bytes.Length);
            return true;
        }
    }
}