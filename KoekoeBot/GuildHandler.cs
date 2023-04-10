using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.VoiceNext;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Linq;
using System.IO;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace KoekoeBot
{
    public class AlarmData
    {
        public string AlarmName;
        public DateTime AlarmDate;
        public int? sampleid = null;
        public ulong userId; //The user that set this alarm, we will try to find this user in the guild's channels.
        public bool recurring; //Not implemented yet
    }

    public class GuildHandler
    {

        public List<ulong> ChannelIds;
        public DiscordGuild Guild;

        // The minimum amount of time between playing bonus clips in minutes
        public int minBonusInterval = 60;
        // The max amount of minutes to be added to minBonusInterval
        public int variableBonusInterval = 30;

        private CancellationTokenSource announceTaskCS = new CancellationTokenSource();

        public bool IsRunning { get; private set; }
        private DiscordClient Client;
        private bool ShouldRun;

        private Dictionary<ulong, DiscordChannel> cachedChannels;

        private Queue<Func<Task>> AnnounceQueue = new Queue<Func<Task>>();

        private VoiceNextConnection cVoiceConnection = null;
        private DebouncedAction debouncedLeave;
        private static int LeaveAfterMs = 20000; //leave after 20seconds of inactivity
        private SavedGuildData guildData;

        List<AlarmData> alarms;

        DateTime nextBonusClip = DateTime.UnixEpoch;
        Random rnd;

        public GuildHandler(DiscordClient client, DiscordGuild guild)
        {
            this.ChannelIds = new List<ulong>();
            this.Client = client;
            this.Guild = guild;
            this.rnd = new Random();

            this.cachedChannels = new Dictionary<ulong, DiscordChannel>();

            Action leaveAction = async () =>
            {
                await this.Leave();
            };
            this.debouncedLeave = leaveAction.Debounce(GuildHandler.LeaveAfterMs);

            alarms = new List<AlarmData>();
        }

        public void Stop()
        {
            ShouldRun = false;
        }

        private void logDebug(string message) {
            ((ILogger<BaseDiscordClient>) this.Client.Logger).LogDebug(message);
        }

        private void logInformation(string message) {
            ((ILogger<BaseDiscordClient>) this.Client.Logger).LogInformation(message);
        }

        private void logWarning(string message) {
            ((ILogger<BaseDiscordClient>) this.Client.Logger).LogWarning(message);
        }

        public void SetGuildData(SavedGuildData data)
        {
            this.guildData = data;
        }
        public void UpdateSamplelist()
        {
            //Create sample list
            var data = this.guildData;
            data.samples = data.samples != null ? data.samples : new List<SampleData>();

            foreach (SampleData sample in data.samples)
            {
                if (!File.Exists(Path.Combine(getSampleBasePath(), sample.Filename)))
                {
                    this.logWarning($"disabling {sample.Name} because {sample.Filename} does not exist");
                    sample.exists = false;
                }
                else
                {
                    sample.exists = true; // TODO: FIXME: we probably want to keep them disabled in some cases even if we have the file
                }
            }

            string[] samplefiles = Directory.EnumerateFiles(Path.Combine(Environment.CurrentDirectory, getSampleBasePath())).Where(x => x.EndsWith(".mp3")).OrderBy(f => File.GetCreationTimeUtc(f)).ToArray();

            //todo: first use slots where the sample file does not exsist anymore.
            //      this way the indecies of exsisting samples don't change when
            //      samples are removed from the list. For now appending new ones
            //      works just fine.
            //      Update: its solved for now by leaving gaps when samples are deleted.
            foreach (string filepath in samplefiles)
            {
                string sampleName = sampleNameFromFilename(filepath);
                SampleData sample = this.guildData.samples.Where(x => x.Filename == Path.GetFileName(filepath)).FirstOrDefault();

                if (sample == null)
                {
                    AddSampleFromFile(filepath, sampleName);
                }
            }

            this.SaveGuildData(false);
        }


        // Make sure not to add duplicates (TODO: might want to add a guard to this method)
        public SampleData AddSampleFromFile(string filepath, string samplename = null)
        {
            string name = samplename != null ? samplename : sampleNameFromFilename(filepath);
            SampleData existing = this.getSample(name);
            if (existing != null)
            {
                this.logWarning($"WARN: {name} already exists in {this.guildData.guildName}");
                return null;
            }

            SampleData nSample = new SampleData();

            nSample.Filename = Path.GetFileName(filepath);
            nSample.Name = name;
            nSample.PlayCount = 0;
            nSample.SampleAliases = new List<string>();
            nSample.SampleAliases.Add(this.guildData.samples.Count.ToString());
            nSample.DateAdded = DateTime.UtcNow;
            nSample.exists = true;
            nSample.enabled = true;

            this.guildData.samples.Add(nSample);

            return nSample;
        }

        public bool AddAlias(string samplename, string alias)
        {
            SampleData existing = getSample(alias);
            if (existing != null)
                return false;

            SampleData sample = getSample(samplename);
            sample.SampleAliases.Add(alias);

            this.SaveGuildData(false);

            return true;
        }

        public bool RemoveAlias(string samplename, string alias)
        {
            SampleData existing = getSample(alias);
            if (existing == null)
                return false;

            existing.SampleAliases.Remove(alias);

            this.SaveGuildData(false);

            return true;
        }

        public SampleData getSample(string nameOrAlias)
        {
            return this.guildData.samples.Where(x => x.Name == nameOrAlias || x.SampleAliases.Contains(nameOrAlias)).FirstOrDefault();
        }

        public async Task<List<DiscordChannel>> GetChannels(List<ulong> ids)
        {
            if (ids == null)
                return null;

            var channels = new List<DiscordChannel>();
            foreach (var channelid in ids)
            {
                DiscordChannel ch = await Client.GetChannelAsync(channelid);
                channels.Add(ch);
                this.cachedChannels[ch.Id] = ch;
            }

            return channels;
        }

        public async Task<List<DiscordChannel>> GetChannelsCached(List<ulong> ids)
        {
            if (ids == null)
                return null;

            List<ulong> uncachedIds = ids.Where(id => !this.cachedChannels.ContainsKey(id)).ToList();
            if (uncachedIds.Count() > 0)
                await GetChannels(uncachedIds); // Puts these channels in the cache so we dont need the return value

            var channels = new List<DiscordChannel>();
            foreach (var channelid in ids)
            {
                channels.Add(cachedChannels.GetValueOrDefault(channelid));
            }

            return channels;
        }

        public string getSampleBasePath()
        {
            return $"volume/samples/{this.Guild.Id}";
        }
        private string getSampleFilePath(string samplename)
        {
            SampleData sample = this.getSample(samplename);
            return getSampleFilePath(sample);
        }
        private string getSampleFilePath(SampleData sample)
        {
            return $"{getSampleBasePath()}/{sample.Filename}";
        }

        private string sampleNameFromFilename(string filename)
        {
            return Path.GetFileName(filename).Replace('_', ' ').Replace(".mp3", "").Replace("extra ", "");
        }

        public string getFileNameForSampleName(string samplename)
        {
            return $"{string.Join("-", $"extra_{samplename.Replace(' ', '_')}".Split(Path.GetInvalidFileNameChars()))}.mp3";
        }

        //When indefinite is true it keeps running until this.ShouldRun is false (aka the guildhandler is stopped)
        public async Task ProcessAnnouncementQueue(Queue<Func<Task>> queue, bool indefinite=true) {
            while(this.ShouldRun) {
                if(queue.Any()){
                    var cTask = queue.Dequeue();
                    if(cTask != null){
                        await cTask();
                    }
                        
                }

                if(!indefinite) {
                    return;
                } else {
                    await Task.Delay(250);
                }
            }
        }

        public async Task Tick() {
            DateTime now = DateTime.Now;

                int alarmCountStart = alarms.Count;
                //Check if we need to trigger an alarm
                for (int i = 0; i < alarms.Count; i++)
                {
                    AlarmData alarm = alarms[i];

                    if (alarm.recurring && alarm.AlarmDate.DayOfWeek != now.DayOfWeek)
                        continue;
                    if (alarm.AlarmDate.Hour == now.Hour && alarm.AlarmDate.Minute == now.Minute)
                    {
                        this.logInformation($"Triggering alarm {alarm.AlarmName} - {alarm.AlarmDate.ToShortTimeString()}");
                        //Announce in the channel where the user that set this alarm currently is
                        List<DiscordChannel> channels = (await GetChannels(this.ChannelIds)).Where(x => x.Users.Where(x => x.Id == alarm.userId).Count() > 0).ToList();
                        string sample = alarm.sampleid != null ? this.getSample(alarm.sampleid.ToString()).Filename : "CHIME1.wav";
                        AnnounceFile(Path.Combine(Environment.CurrentDirectory, this.getSampleBasePath(), sample), 2, channels, true);
                        alarms.RemoveAt(i); //Todo: implement recurring alarms
                        i--;
                    }
                }

                if (alarms.Count != alarmCountStart)
                {
                    SaveGuildData(); //save the alarms list if it has changed
                }

                //Special case for 420 (blaze it)
                if (now.Minute == 20 && (now.Hour % 12) == 4)
                {
                    AnnounceFile(Path.Combine(Environment.CurrentDirectory, this.getSampleBasePath(), "420.mp3"), 1, null, true);
                }

                if (now.Minute == 0) //If we entered a new hour
                {
                    this.logInformation($"Guildhandler for {this.guildData.guildName} entered new hour: {now.Hour}");
                    this.SaveGuildData();
                    AnnounceFile(getFileNameForHour(now.Hour), 1, null, true);
                }

                //check for bonus clip
                if (nextBonusClip - now <= TimeSpan.Zero)
                {
                    //Play bonus clip
                    string[] extraClipFiles = Directory.EnumerateFiles(Path.Combine(Environment.CurrentDirectory, this.getSampleBasePath())).Where(x => x.Contains("extra_")).ToArray();
                    int clipIndex = rnd.Next(extraClipFiles.Length);

                    if (nextBonusClip != DateTime.UnixEpoch)//Ignore the first time as this runs when the handler is started
                        AnnounceFile(extraClipFiles[clipIndex], 1, null, true);

                    //Determine when we next will play a bonusclip (from minBonusInterval up to minBonusInterval + variableBonusInterval minutes)

                    nextBonusClip = now.AddMinutes(this.minBonusInterval + (int)(rnd.NextDouble() * this.variableBonusInterval));
                    this.logInformation($"Selected {extraClipFiles[clipIndex]} as bonus clip for {this.Guild.Name} which will be played at {nextBonusClip.ToShortTimeString()}");
                }
                                
        }


        // Deprecated way to run an indefinite loop calling tick every minute.
        public async Task Execute()
        {
            DateTime now = DateTime.Now;

            this.logInformation($"Executing timekeeper loop for {this.guildData.guildName}");
            IsRunning = true;
            ShouldRun = true;

            // Will run a background task working on the announcement queue tasks
            _ = Task.Factory.StartNew(async () => { await ProcessAnnouncementQueue(this.AnnounceQueue); }, TaskCreationOptions.LongRunning);
            
            //This loop ticks every new minute on the systemclock
            while (ShouldRun)
            {
                await Tick();

                //Calculate the number of miliseconds until a new minute on the system clock (fix? add one second to account for task.delay() inaccuracy)
                double millisToNextMinute = (double)((60 * 1000) - now.TimeOfDay.TotalMilliseconds % (60 * 1000));
                await Task.Delay((int)millisToNextMinute + 1000);
            }

            this.logInformation($"Guildhandler stopped {Guild.Name}");

            IsRunning = false;
        }

        public void AnnounceSample(string sampleName, int loopcount = 1, List<DiscordChannel> channels = null)
        {
            SampleData guildSample = this.getSample(sampleName);
            if (guildSample == null)
            {
                this.logWarning("not playing sample; guildSample is null");
                return;
            }

            guildSample.PlayCount++;

            this.AnnounceFile(getSampleFilePath(guildSample), loopcount, channels);
        }

        public async Task<VoiceNextConnection> JoinWithVoice(DiscordChannel channel)
        {
            if (cVoiceConnection != null && cVoiceConnection.TargetChannel.Id == channel.Id)
            {
                this.logDebug("Reusing voice connection");
                return this.cVoiceConnection;
            }
            else
            {
                if (cVoiceConnection != null)
                {
                    this.logDebug($"Leaving a channel before joining {channel.Name}");
                    await this.Leave(this.cVoiceConnection);
                }
            }

            // check whether VNext is enabled
            var vnext = Client.GetVoiceNext();
            if (vnext == null)
            {
                this.logWarning("VoiceNext not configured");
                return null;
            }

            // check whether we aren't already connected
            var vnc = vnext.GetConnection(channel.Guild);
            if (vnc != null)
            {
                this.logWarning("Already connected in this guild.");
                return null;
            }

            // connect
            this.logDebug("vnext.ConnectAsync");
            vnc = await vnext.ConnectAsync(channel);

            this.cVoiceConnection = vnc;

            await Task.Delay(500);

            return vnc;
        }

        public async Task Leave(VoiceNextConnection voiceConnection = null)
        {
            voiceConnection = voiceConnection != null ? voiceConnection : this.cVoiceConnection;

            if (voiceConnection != null)
            {
                var vnext = Client.GetVoiceNext();
                await voiceConnection.SendSpeakingAsync(false);
                vnext.GetConnection(voiceConnection.TargetChannel.Guild).Disconnect();

                this.cVoiceConnection = null;
                this.isPlaying = false;
            } else {
                this.logWarning("Connection = null while trying to leaving channel");
            }
        }
        bool isPlaying = false;


        // Queues a job that joins channel(s), plays an audio file and leaves again.
        // when forceQueue is true it ignores the limit on the queue length (so we can queue hour announcements etc.)
        public void AnnounceFile(string audio_path, int loopcount = 1, List<DiscordChannel> channels = null, bool forceQueue = false)
        {
            if(!forceQueue && this.AnnounceQueue.Count() > 5) {
                this.logInformation("queue is full, ignoring AnnounceFile enqueuement..");
                return; 
            }

            Func<Task> nAnnounceTask = async () => {
                if(this.isPlaying)
                {
                    this.logWarning("WARNING: Already playing, ya done goofed..");
                }
                

                if (channels == null)
                    channels = (await GetChannels(this.ChannelIds));

                if (!File.Exists(audio_path))
                {
                    this.logWarning($"Will not be playing {audio_path} (file not found)");
                    return;
                }

                CancellationToken ct = announceTaskCS.Token;

                foreach (DiscordChannel channel in channels)
                {
                    if (channel.Users.Count() == 0)
                    {
                        this.logWarning($"Will not be playing {audio_path} in {channel.Guild.Name}/{channel.Name} (no users online)");
                        continue; //skip empty channels
                    }
                    var vnc = await JoinWithVoice(channel);
                    

                    if (vnc == null)
                    {
                        this.logWarning($"Will not be playing {audio_path} in {channel.Guild.Name}/{channel.Name} (problem joining the channel)");
                        continue;
                    }
                    
                    this.debouncedLeave.action();
                    isPlaying = true;

                    try
                    {
                        // wait for current playback to finish
                        while (vnc.IsPlaying)
                        {
                            if(ct.IsCancellationRequested)
                            {
                                await this.Leave();
                                return;
                            }
                            this.logDebug("WaitForPlaybackFinishAsync..");
                            await vnc.WaitForPlaybackFinishAsync();
                        }

                        await vnc.SendSpeakingAsync(true);

                        var psi = new ProcessStartInfo
                        {
                            FileName = RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "ffmpeg" : "ffmpeg.exe",
                            Arguments = $@"-i ""{audio_path}"" -ac 2 -f s16le -ar 48000 pipe:1 -loglevel quiet",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };

                        this.logInformation($"Playing {audio_path} in {channel.Guild.Name}/{channel.Name}");

                        for (int i = 0; i < loopcount; i++)
                        {
                            var ffmpeg = Process.Start(psi);
                            var ffout = ffmpeg.StandardOutput.BaseStream;

                            var txStream = vnc.GetTransmitSink();
                            await ffout.CopyToAsync(txStream);
                            await txStream.FlushAsync();
                            await vnc.WaitForPlaybackFinishAsync();
                        }

                        await Task.Delay(100);
                    }
                    catch (Exception ex) { Console.Write(ex.StackTrace); this.Leave(); }
                    finally
                    {
                        this.logInformation("finished playing sample");
                        this.debouncedLeave.action();
                        isPlaying = false;
                    }

                }
            };
        
            this.AnnounceQueue.Enqueue(nAnnounceTask); // In Execute a background task was kicked off to work off this queue..
        }

        public void AddChannel(DiscordChannel channel, bool autosave = true)
        {
            if (!ChannelIds.Contains(channel.Id))
            {
                ChannelIds.Add(channel.Id);

                if (autosave)
                    this.SaveGuildData();
            }
        }

        public void AddAlarm(DateTime alarmdate, string alarmname, int? sampleId, ulong userid, bool autosave = true)
        {
            AlarmData nAlarm = new AlarmData()
            {
                AlarmDate = alarmdate,
                AlarmName = alarmname,
                sampleid = sampleId,
                userId = userid
            };

            this.alarms.Add(nAlarm);

            if (autosave)
                this.SaveGuildData();
        }

        public List<AlarmData> GetAlarms()
        {
            return this.alarms;
        }

        public void RemoveChannel(DiscordChannel channel)
        {
            if (ChannelIds.Remove(channel.Id))
            {
                SaveGuildData();
            }
        }

        public async Task<string[]> GetRegisteredChannelNames()
        {
            List<DiscordChannel> channels = await this.GetChannels(this.ChannelIds);
            return channels.Select(x => x.Name).ToArray();
        }

        public ulong[] GetRegisteredChannelIds()
        {
            return this.ChannelIds.ToArray();
        }

        public SavedGuildData GetGuildData()
        {
            return this.guildData;
        }

        public void SaveGuildData(bool silent = true)
        {
            SavedGuildData data = this.guildData != null ? this.guildData : new SavedGuildData();
            data.alarms = this.alarms.ToArray();
            data.channelIds = this.GetRegisteredChannelIds();

            //Update saved data
            string data_path = Path.Combine(Environment.CurrentDirectory, "volume", "data", $"guilddata_{this.Guild.Id}.json");
            if (!File.Exists(data_path))
            {
                File.Create(data_path).Close();
            }
            File.WriteAllText(data_path, JsonConvert.SerializeObject(data));

            if (!silent)
                this.logInformation($"Saved guild data to {data_path}");
        }

        public void ClearGuildData()
        {
            this.alarms.Clear();
            this.ChannelIds.Clear();

            //Delete saved data file
            string data_path = Path.Combine(Environment.CurrentDirectory, "data", $"guilddata_{this.Guild.Id}.json");
            if (!File.Exists(data_path))
            {
                File.Delete(data_path);
            }
        }

        public KoekoeDiscordId getDiscordId()
        {
            return new KoekoeDiscordId() { Id = this.Guild.Id.ToString(), Name = this.Guild.Name };
        }

        private string getFileNameForHour(int hour)
        {
            string sampleDir = Path.Combine(Environment.CurrentDirectory, getSampleBasePath());
            string[] allClips = Directory.EnumerateFiles(sampleDir).ToArray();
            string f = Path.GetRelativePath(sampleDir, allClips[0]);
            string[] hourClipFiles = allClips.Where(x => Path.GetRelativePath(sampleDir, x).StartsWith($"{hour % 12}_uur") && x.EndsWith(".mp3")).ToArray();
            int clipIndex = rnd.Next(hourClipFiles.Length);

            return hourClipFiles[clipIndex];
        }


    }
}
