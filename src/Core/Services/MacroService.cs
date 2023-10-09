﻿using Blish_HUD;
using Flurl.Http;
using Gw2Sharp.WebApi.V2.Models;
using Microsoft.Xna.Framework;
using Nekres.ChatMacros.Core.Services.Data;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Nekres.ChatMacros.Core.Services {
    internal class MacroService : IDisposable {

        private const char  COMMAND_START = '{';
        private const char  COMMAND_END   = '}';
        private const char  PARAM_CHAR    = ' ';
        private       Regex _commandRegex = new (@$"\{COMMAND_START}(?<command>[^\{COMMAND_END}]+)\{COMMAND_END}", RegexOptions.Compiled);
        private       Regex _paramRegex   = new($"{PARAM_CHAR}(?<param>[^{PARAM_CHAR}]+)", RegexOptions.Compiled);

        private IReadOnlyList<ContinentFloorRegionMap> _regionMaps;

        public IReadOnlyList<Map>         AllMaps         { get; private set; }
        public Map                        CurrentMap      { get; private set; }
        public ContinentFloorRegionMapPoi ClosestWaypoint { get; private set; }
        public ContinentFloorRegionMapPoi ClosestPoi      { get; private set; }
        public IReadOnlyList<BaseMacro>   ActiveMacros    { get; private set; }
        
        public MacroService() {
            ActiveMacros = new List<BaseMacro>();
            UpdateMacros();
            GameService.Gw2Mumble.CurrentMap.MapChanged += OnMapChanged;
            OnMapChanged(this, new ValueEventArgs<int>(GameService.Gw2Mumble.CurrentMap.Id));
            GameService.Overlay.UserLocaleChanged += OnUserLocaleChanged;
        }

        private async void OnUserLocaleChanged(object sender, ValueEventArgs<CultureInfo> e) {
            AllMaps = await ChatMacros.Instance.Gw2Api.GetMaps();
        }

        public void UpdateMacros() {
            ToggleMacros(false);
            ActiveMacros = ChatMacros.Instance.Data.GetActiveMacros();
            ToggleMacros(true);
        }

        public void ToggleMacros(bool enabled) {
            foreach (var macro in ActiveMacros) {
                macro.Toggle(enabled);
            }
        }

        public void Update(GameTime gameTime) {
            GetClosestPoints();
        }

        private async void OnMapChanged(object sender, ValueEventArgs<int> e) {
            UpdateMacros();

            if (!ChatMacros.Instance.Gw2Api.IsApiAvailable()) {
                return;
            }

            if (AllMaps.IsNullOrEmpty()) {
                AllMaps = await ChatMacros.Instance.Gw2Api.GetMaps();
            }

            var currentMap = AllMaps.FirstOrDefault(map => map.Id == e.Value);
            if (currentMap == null) {
                return;
            }
            CurrentMap = currentMap;
            _regionMaps = await ChatMacros.Instance.Gw2Api.GetRegionMap(CurrentMap);
        }

        private void GetClosestPoints() {
            if (_regionMaps.IsNullOrEmpty() || CurrentMap == null) {
                return;
            }
            var pois = _regionMaps.Where(x => x != null).SelectMany(x => x.PointsOfInterest.Values.Distinct()).ToList();

            var continentPosition = GameService.Gw2Mumble.RawClient.AvatarPosition.ToContinentCoords(CoordsUnit.MUMBLE, CurrentMap.MapRect, CurrentMap.ContinentRect);
            
            double closestPoiDistance      = double.MaxValue;
            double closestWaypointDistance = double.MaxValue;
            foreach (var poi in pois) {
                double distanceX               = Math.Abs(continentPosition.X     - poi.Coord.X);
                double distanceZ               = Math.Abs(continentPosition.Z     - poi.Coord.Y);
                double distance                = Math.Sqrt(Math.Pow(distanceX, 2) + Math.Pow(distanceZ, 2));

                switch (poi.Type.Value) {
                    case PoiType.Waypoint when distance < closestWaypointDistance:
                        closestWaypointDistance = distance;
                        ClosestWaypoint         = poi;
                        break;
                    case PoiType.Landmark when distance < closestPoiDistance:
                        closestPoiDistance = distance;
                        ClosestPoi         = poi;
                        break;
                }
            }
        }

        public async Task<string> ReplaceCommands(string text) {
            var matches = _commandRegex.Matches(text);
            var result = text;
            foreach (Match match in matches) {
                var command = match.Groups["command"].Value;

                var replacement = await Resolve(command);
                if (string.IsNullOrWhiteSpace(replacement)) {
                    return string.Empty;
                }

                result = result.Replace($"{COMMAND_START}{command}{COMMAND_END}", replacement);
            }
            return result;
        }

        private async Task<string> Resolve(string fullCommand) {
            var matches = _paramRegex.Matches(fullCommand);

            var args = new List<string>();
            foreach (Match match in matches) {
                var arg = match.Groups["param"].Value;
                args.Add(arg);
            }

            var paramsStart = fullCommand.IndexOf(PARAM_CHAR);

            var command = paramsStart < 0 ? fullCommand : fullCommand.Substring(0, paramsStart);
            
            return await Exec(command, args);
        }

        private async Task<string> Exec(string command, IReadOnlyList<string> args) {
            return command switch {
                "blish"  => GetVersion(),
                "time"   => DateTime.Now.ToString("HH:mm",          CultureInfo.CurrentUICulture),
                "today"  => DateTime.Now.ToString("dddd, d.M.yyyy", CultureInfo.CurrentUICulture),
                "wp"     => ClosestWaypoint != null ? ClosestWaypoint.ChatLink : string.Empty,
                "poi"    => ClosestPoi      != null ? ClosestPoi.ChatLink : string.Empty,
                "map"    => CurrentMap      != null ? CurrentMap.Name : string.Empty,
                "random" => GetRandom(args).ToString(),
                "json"   => await GetJson(args),
                "txt"    => ReadTextFile(args),
                _        => string.Empty
            };
        }

        private string ReadTextFile(IReadOnlyList<string> args) {
            if (args.Count == 0) {
                return string.Empty;
            }

            var path = args[0].Replace("%20", " ");

            try {
                if (!System.IO.File.Exists(path)) {
                    return string.Empty;
                }

                var lines = System.IO.File.ReadAllLines(path);

                if (lines.IsNullOrEmpty()) {
                    return string.Empty;
                }

                int line = RandomUtil.GetRandom(0, lines.Length - 1);

                if (args.Count == 2 && int.TryParse(args[1], out line)) {
                    line = line < lines.Length - 1 ? line : RandomUtil.GetRandom(0, lines.Length - 1);
                }
                return lines[line];
            } catch (Exception e) {
                ChatMacros.Logger.Error(e, e.Message);
                return string.Empty;
            }
        }

        private string GetVersion() {
            var version = typeof(BlishHud).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            var release = string.IsNullOrEmpty(version) ? string.Empty : $"Blish HUD v{version.Split('+').First()}";
            return release;
        }

        private int GetRandom(IReadOnlyList<string> args) {
            int max = int.MaxValue;
            int min = 0;
            if (args.Count > 0) {
                if (args.Count == 1) {
                    int.TryParse(args[0], out max);
                } else if (args.Count == 2) {
                    int.TryParse(args[0], out min);
                    int.TryParse(args[1], out max);
                }
            }
            return RandomUtil.GetRandom(min, max);
        }

        private async Task<string> GetJson(IReadOnlyList<string> args) {
            if (args.Count < 2) {
                return string.Empty;
            }

            var url = args[1];

            if (!url.IsWebLink()) {
                return string.Empty;
            }

            var response = await HttpUtil.TryAsync(() => url.GetStringAsync());

            var path = args[0];

            return JsonPropertyUtil.GetPropertyFromJson(response, path);
        }

        public void Dispose() {
            GameService.Overlay.UserLocaleChanged -= OnUserLocaleChanged;
            ToggleMacros(false);
        }
    }
}