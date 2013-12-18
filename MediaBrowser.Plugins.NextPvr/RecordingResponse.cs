﻿using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.LiveTv;
using MediaBrowser.Model.Serialization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace MediaBrowser.Plugins.NextPvr
{
    public class RecordingResponse
    {
        private readonly CultureInfo _usCulture = new CultureInfo("en-US");
        private readonly string _baseUrl;

        public RecordingResponse(string baseUrl)
        {
            _baseUrl = baseUrl;
        }

        public IEnumerable<RecordingInfo> GetRecordings(Stream stream, IJsonSerializer json)
        {
            if (stream == null)
            {
                throw new ArgumentNullException("stream");
            }

            var root = json.DeserializeFromStream<RootObject>(stream);

            return root.ManageResults
                .EPGEvents
                .Select(i => i.epgEventJSONObject)

                // Seeing recurring parents coming back with these reponses, for some reason
                .Where(i => i.schd != null)
                .Select(GetRecordingInfo);
        }

        public IEnumerable<TimerInfo> GetTimers(Stream stream, IJsonSerializer json)
        {
            if (stream == null)
            {
                throw new ArgumentNullException("stream");
            }

            var root = json.DeserializeFromStream<RootObject>(stream);

            return root.ManageResults
                .EPGEvents
                .Select(i => i.epgEventJSONObject)

                // Seeing recurring parents coming back with these reponses, for some reason
                .Where(i => i.schd != null)
                .Select(GetTimerInfo);
        }

        public IEnumerable<SeriesTimerInfo> GetSeriesTimers(Stream stream, IJsonSerializer json)
        {
            if (stream == null)
            {
                throw new ArgumentNullException("stream");
            }

            var root = json.DeserializeFromStream<RootObject>(stream);

            return root.ManageResults
                .EPGEvents
                .Select(i => i.epgEventJSONObject)

                // Seeing recurring parents coming back with these reponses, for some reason
                .Where(i => i.recurr != null)
                .Select(GetSeriesTimerInfo);
        }

        private RecordingInfo GetRecordingInfo(EpgEventJSONObject i)
        {
            var info = new RecordingInfo();

            var recurr = i.recurr;
            if (recurr != null)
            {
                info.ChannelName = GetChannelName(recurr);
            }

            var schd = i.schd;

            if (schd != null)
            {
                info.ChannelId = schd.ChannelOid.ToString(_usCulture);
                info.Id = schd.OID.ToString(_usCulture);
                info.Path = schd.RecordingFileName;
                info.Url = string.IsNullOrEmpty(schd.DownloadURL) ? null : (_baseUrl + schd.DownloadURL);
                info.Status = ParseStatus(schd.Status);
                info.StartDate = DateTime.Parse(schd.StartTime);
                info.EndDate = DateTime.Parse(schd.EndTime);

                info.IsHD = string.Equals(schd.Quality, "hdtv", StringComparison.OrdinalIgnoreCase);
            }

            var epg = i.epgEvent;

            if (epg != null)
            {
                info.Audio = ListingsResponse.ParseAudio(epg.Audio);
                info.ProgramId = epg.OID.ToString(_usCulture);
                info.OfficialRating = epg.Rating;
                info.EpisodeTitle = epg.Subtitle;
                info.Name = epg.Title;
                info.Overview = epg.Desc;
                info.Genres = epg.Genres;
                info.IsRepeat = !epg.FirstRun;
                info.CommunityRating = ListingsResponse.ParseCommunityRating(epg.StarRating);
                info.IsHD = string.Equals(epg.Quality, "hdtv", StringComparison.OrdinalIgnoreCase);
            }

            return info;
        }

        private TimerInfo GetTimerInfo(EpgEventJSONObject i)
        {
            var info = new TimerInfo();

            var recurr = i.recurr;
            if (recurr != null)
            {
                info.ChannelName = GetChannelName(recurr);

                info.SeriesTimerId = recurr.OID.ToString(_usCulture);
            }

            var schd = i.schd;

            if (schd != null)
            {
                info.ChannelId = schd.ChannelOid.ToString(_usCulture);
                info.Id = schd.OID.ToString(_usCulture);
                info.Status = ParseStatus(schd.Status);
                info.StartDate = DateTime.Parse(schd.StartTime);
                info.EndDate = DateTime.Parse(schd.EndTime);

                info.PrePaddingSeconds = int.Parse(schd.PrePadding, _usCulture) * 60;
                info.PostPaddingSeconds = int.Parse(schd.PostPadding, _usCulture) * 60;
            }

            var epg = i.epgEvent;

            if (epg != null)
            {
                info.ProgramId = epg.OID.ToString(_usCulture);
                info.Name = epg.Title;
                info.Overview = epg.Desc;
            }

            return info;
        }

        private SeriesTimerInfo GetSeriesTimerInfo(EpgEventJSONObject i)
        {
            var info = new SeriesTimerInfo();

            var recurr = i.recurr;
            if (recurr != null)
            {
                info.ChannelName = GetChannelName(recurr);
                info.ChannelId = GetChannelId(recurr);

                info.Id = recurr.OID.ToString(_usCulture);

                info.StartDate = DateTime.Parse(recurr.StartTime);
                info.EndDate = DateTime.Parse(recurr.EndTime);

                info.PrePaddingSeconds = int.Parse(recurr.PrePadding, _usCulture) * 60;
                info.PostPaddingSeconds = int.Parse(recurr.PostPadding, _usCulture) * 60;

                info.Name = recurr.RecurringName ?? recurr.EPGTitle;
                info.RecordNewOnly = recurr.OnlyNew;
                info.RecordAnyChannel = recurr.allChannels;

                info.Days = (recurr.Day ?? string.Empty).Split(',')
                    .Select(d => (DayOfWeek)Enum.Parse(typeof(DayOfWeek), d.Trim(), true))
                    .ToList();

                info.Priority = recurr.Priority;
            }

            var epg = i.epgEvent;

            if (epg != null)
            {
                info.ProgramId = epg.OID.ToString(_usCulture);
                info.Overview = epg.Desc;
            }

            return info;
        }

        private string GetChannelName(Recurr recurr)
        {
            if (recurr.RulesXmlDoc != null && recurr.RulesXmlDoc.Rules != null)
            {
                return string.Equals(recurr.RulesXmlDoc.Rules.ChannelName, "All Channels", StringComparison.OrdinalIgnoreCase) ? null : recurr.RulesXmlDoc.Rules.ChannelName;
            }

            return null;
        }

        private string GetChannelId(Recurr recurr)
        {
            if (recurr.RulesXmlDoc != null && recurr.RulesXmlDoc.Rules != null)
            {
                return string.Equals(recurr.RulesXmlDoc.Rules.ChannelOID, "0", StringComparison.OrdinalIgnoreCase) ? null : recurr.RulesXmlDoc.Rules.ChannelOID;
            }

            return null;
        }

        private RecordingStatus ParseStatus(string value)
        {
            if (string.Equals(value, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                return RecordingStatus.Completed;
            }

            if (string.Equals(value, "In-Progress", StringComparison.OrdinalIgnoreCase))
            {
                return RecordingStatus.InProgress;
            }

            if (string.Equals(value, "Failed", StringComparison.OrdinalIgnoreCase))
            {
                return RecordingStatus.Error;
            }

            if (string.Equals(value, "Conflict", StringComparison.OrdinalIgnoreCase))
            {
                return RecordingStatus.ConflictedNotOk;
            }

            if (string.Equals(value, "Deleted", StringComparison.OrdinalIgnoreCase))
            {
                return RecordingStatus.Cancelled;
            }
            
            return RecordingStatus.Scheduled;
        }

        private class Rules
        {
            public string ChannelOID { get; set; }
            public string ChannelName { get; set; }
            public string StartTime { get; set; }
            public string EndTime { get; set; }
            public string PrePadding { get; set; }
            public string PostPadding { get; set; }
            public string Quality { get; set; }
            public string Keep { get; set; }
            public string Days { get; set; }
            public string EPGTitle { get; set; }

        }

        private class RulesXmlDoc
        {
            public Rules Rules { get; set; }
        }

        private class Recurr
        {
            public string Type { get; set; }
            public int OID { get; set; }
            public string RecurringName { get; set; }
            public string PeriodDescription { get; set; }
            public string EPGTitle { get; set; }
            public int ChannelOid { get; set; }
            public string StartTime { get; set; }
            public string EndTime { get; set; }
            public object RecordingDirectoryID { get; set; }
            public int Priority { get; set; }
            public string Quality { get; set; }
            public string PrePadding { get; set; }
            public string PostPadding { get; set; }
            public string MaxRecordings { get; set; }
            public bool allChannels { get; set; }
            public bool OnlyNew { get; set; }
            public string Day { get; set; }
            public object AdvancedRules { get; set; }
            public RulesXmlDoc RulesXmlDoc { get; set; }

        }

        private class Rtn
        {
            public bool Error { get; set; }
            public string Message { get; set; }
        }

        private class EpgEvent2
        {
            public int OID { get; set; }
            public string UniqueId { get; set; }
            public int ChannelOid { get; set; }
            public string StartTime { get; set; }
            public string EndTime { get; set; }
            public string Title { get; set; }
            public string Subtitle { get; set; }
            public string Desc { get; set; }
            public string Rating { get; set; }
            public string Quality { get; set; }
            public string StarRating { get; set; }
            public string Aspect { get; set; }
            public string Audio { get; set; }
            public string OriginalAirdate { get; set; }
            public string FanArt { get; set; }
            public List<string> Genres { get; set; }
            public bool FirstRun { get; set; }
            public bool HasSchedule { get; set; }
            public bool ScheduleIsRecurring { get; set; }

        }

        private class Schd
        {
            public int OID { get; set; }
            public int ChannelOid { get; set; }
            public int Priority { get; set; }
            public string Name { get; set; }
            public string Quality { get; set; }
            public string Type { get; set; }
            public string Day { get; set; }
            public string StartTime { get; set; }
            public string EndTime { get; set; }
            public string Status { get; set; }
            public string FailureReason { get; set; }
            public string PrePadding { get; set; }
            public string PostPadding { get; set; }
            public string MaxRecordings { get; set; }
            public string DownloadURL { get; set; }
            public string RecordingFileName { get; set; }
            public int PlaybackPosition { get; set; }
            public int PlaybackDuration { get; set; }
            public string LastWatched { get; set; }
            public bool OnlyNew { get; set; }
            public bool Blue { get; set; }
            public bool Green { get; set; }
            public bool Red { get; set; }
            public bool Yellow { get; set; }
            public string FanArt { get; set; }
        }

        private class EpgEventJSONObject
        {
            public Recurr recurr { get; set; }
            public Rtn rtn { get; set; }
            public EpgEvent2 epgEvent { get; set; }
            public Schd schd { get; set; }
        }

        private class EPGEvent
        {
            public EpgEventJSONObject epgEventJSONObject { get; set; }
        }

        private class ManageResults
        {
            public List<EPGEvent> EPGEvents { get; set; }
        }

        private class RootObject
        {
            public ManageResults ManageResults { get; set; }
        }
    }
}
