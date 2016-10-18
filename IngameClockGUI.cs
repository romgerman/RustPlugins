using System;
using System.Collections.Generic;

using Oxide.Core;

/* --- Do not edit anything here if you don't know what are you doing --- */

namespace Oxide.Plugins
{
	[Info("IngameClockGUI", "deer_SWAG", "0.0.51", ResourceId = 1245)]
	[Description("Displays ingame and server time")]
	public class IngameClockGUI : RustPlugin
	{
		const string defaultInfoSize = "0.3";

		const int isClockEnabled = 1 << 0;
		const int isServerTime = 1 << 1;

		class StoredData
		{
			public HashSet<Player> Players = new HashSet<Player>();

			public StoredData() { }
		}

		class Player
		{
			public ulong UserID;
			public int Options;

			public Player() { }
			public Player(ulong id, int options)
			{
				UserID = id;
				Options = options;
			}
		}

		private class TimedInfo
		{
			public long startTime;
			public long endTime;
			public string text;
			public bool serverTime;
			public string size;

			public TimedInfo(long st, long et, string txt, bool server, string s)
			{
				startTime = st;
				endTime = et;
				text = txt;
				serverTime = server;
				size = s;
			}
		}

		private string clockJson = @"
		[{
			""name"":   ""Clock"",
			""parent"": ""Overlay"",
			""components"":
			[
				{
					""type"":	   ""UnityEngine.UI.Button"",
					""color"":	   ""%background%"",
					""imagetype"": ""Tiled""
				},
				{
					""type"":	   ""RectTransform"",
					""anchormin"": ""%left% %bottom%"",
					""anchormax"": ""%right% %top%""
				}
			]
		},
		{
			""parent"": ""Clock"",
			""components"":
			[
				{
					""type"":	  ""UnityEngine.UI.Text"",
					""text"":	  ""%prefix%%time%%postfix%"",
					""fontSize"": %size%,
					""color"":    ""%color%"",
					""align"":    ""MiddleCenter""
				},
				{
					""type"":	   ""RectTransform"",
					""anchormin"": ""0 0"",
					""anchormax"": ""1 0.9""
				}
			]
		}]";

		private string infoJson = @"
		[{
			""name"":   ""ClockInfo"",
			""parent"": ""Overlay"",
			""components"":
			[
				{
					""type"":	   ""UnityEngine.UI.Button"",
					""color"":	   ""%background%"",
					""imagetype"": ""Tiled""
				},
				{
					""type"":	   ""RectTransform"",
					""anchormin"": ""%info_left% %bottom%"",
					""anchormax"": ""%info_right% %top%""
				}
			]
		},
		{
			""parent"": ""ClockInfo"",
			""components"":
			[
				{
					""type"":	  ""UnityEngine.UI.Text"",
					""text"":	  ""%info%"",
					""fontSize"": %size%,
					""color"":    ""%color%"",
					""align"":    ""MiddleCenter""
				},
				{
					""type"":	   ""RectTransform"",
					""anchormin"": ""0.01 0"",
					""anchormax"": ""0.99 1""
				}
			]
		}]";

		// -------------------- MAIN --------------------

		StoredData 		data;
		Timer 		updateTimer;
		TOD_Sky 	sky;
		DateTime 	dt;

		bool isLoaded = false,
			 isInit   = false;

		string   time = "";
		DateTime gameTime;
		DateTime serverTime;

		private TimedInfo 		currentTI;
		private List<TimedInfo> tiList;

		protected override void LoadDefaultConfig()
		{
			Config.Clear();

			CheckConfig();
			Puts("Default config was saved and loaded!");
		}

		void Loaded()
		{
			isLoaded = true;
			if(isInit) Load();
		}

		void OnServerInitialized()
		{
			isInit = true;
			if(isLoaded) Load();
		}

		void Load()
		{
			data = Interface.GetMod().DataFileSystem.ReadObject<StoredData>(Title);
			tiList = new List<TimedInfo>();
			currentTI = null;
			sky  = TOD_Sky.Instance;

			CheckConfig();

			double left   = (double)Config["Position", "Left"];
			double right  = (double)Config["Position", "Left"] + (double)Config["Size", "Width"];
			double bottom = (double)Config["Position", "Bottom"];
			double top    = (double)Config["Position", "Bottom"] + (double)Config["Size", "Height"];

			clockJson = clockJson.Replace("%background%", (string)Config["BackgroundColor"])
								 .Replace("%color%", (string)Config["TextColor"])
								 .Replace("%size%", Config["FontSize"].ToString())
								 .Replace("%left%", left.ToString())
								 .Replace("%right%", right.ToString())
								 .Replace("%bottom%", bottom.ToString())
								 .Replace("%top%", top.ToString())
								 .Replace("%prefix%", (string)Config["Prefix"])
								 .Replace("%postfix%", (string)Config["Postfix"]);

			// --- for timed notifications

			List<object> ti = (List<object>)Config["TimedInfo"];
			int size = ti.Count;

			for(int i = 0; i < size; i++)
			{
				string infoString = (string)ti[i];

				if(infoString.Length > 0)
					tiList.Add(GetTimedInfo(infoString));
			}

			double info_left = right + 0.002;

			infoJson = infoJson.Replace("%background%", (string)Config["BackgroundColor"])
							   .Replace("%color%", (string)Config["TextColor"])
							   .Replace("%size%", Config["FontSize"].ToString())
							   .Replace("%bottom%", bottom.ToString())
							   .Replace("%top%", top.ToString())
							   .Replace("%info_left%", info_left.ToString())
							   .Replace("%info_right%", defaultInfoSize);
			// ---

			UpdateTime();

			updateTimer = timer.Repeat((int)Config["UpdateTimeInSeconds"], 0, () => UpdateTime());
		}

		void OnPlayerInit(BasePlayer player)
		{
			//currentTI = null;
			//UpdateInfo();
		}

		void Unload()
		{
			SaveData();
			DestroyGUI();
			DestroyInfo();
		}

		[ChatCommand("clock")]
		void cmdChat(BasePlayer player, string command, string[] args)
		{
			if(args.Length == 1)
			{
				if(args[0] == "server" || args[0] == "s")
				{
					if((bool)Config["PreventChangingTime"])
						PrintToChat(player, (string)Config["Messages", "PreventChangeEnabled"]);
					else
						if(data.Players.Count > 0)
						{
							foreach(Player p in data.Players)
							{
								if(p.UserID == player.userID)
								{
									if(GetOption(p.Options, isServerTime))
									{
										p.Options &= ~isServerTime;
										PrintToChat(player, (string)Config["Messages", "STDisabled"]);
									}
									else
									{
										p.Options += isServerTime;
										PrintToChat(player, (string)Config["Messages", "STEnabled"]);
									}

									break;
								}
							}
						}
						else
						{
							data.Players.Add(new Player(player.userID, isClockEnabled | isServerTime));
							PrintToChat(player, (string)Config["Messages", "STEnabled"]);
						}
				}
				else
				{
					SendHelpText(player);
				}
			}
			else
			{
				bool found = false;

				if(data.Players.Count > 0)
				{
					foreach(Player p in data.Players)
					{
						if(p.UserID == player.userID)
						{
							found = true;

							if(GetOption(p.Options, isClockEnabled))
							{
								p.Options &= ~isClockEnabled;
								DestroyGUI();
								PrintToChat(player, (string)Config["Messages", "Disabled"]);
							}
							else
							{
								p.Options += isClockEnabled;
								AddGUI();
								PrintToChat(player, (string)Config["Messages", "Enabled"]);
							}

							break;
						}
						else
						{
							found = false;
						}
					}

					if(!found)
					{
						data.Players.Add(new Player(player.userID, 0));
						DestroyGUI();
						PrintToChat(player, (string)Config["Messages", "Disabled"]);
					}
				}
				else
				{
					data.Players.Add(new Player(player.userID, 0));
					PrintToChat(player, (string)Config["Messages", "Disabled"]);
				}
			}
		}

		void AddGUI()
		{
			if(data.Players.Count > 0)
			{
				int size = BasePlayer.activePlayerList.Count;
				for(int i = 0; i < size; i++)
				{
					BasePlayer bp = BasePlayer.activePlayerList[i];
					bool found = false;

					foreach(Player p in data.Players)
					{
						if(p.UserID == bp.userID)
						{
							found = true;

							if(GetOption(p.Options, isClockEnabled))
							{
								if(!((bool)Config["PreventChangingTime"]))
									if(GetOption(p.Options, isServerTime))
										dt = serverTime;
									else
										dt = gameTime;

								ShowTime();

								SendClientCommand(bp, "AddUI", new Facepunch.ObjectList(clockJson.Replace("%time%", time)));
							}

							break;
						}
						else
						{
							found = false;
						}
					}

					if(!found)
					{
						if(!((bool)Config["PreventChangingTime"]))
							dt = gameTime;
						ShowTime();
						SendClientCommand(bp, "AddUI", new Facepunch.ObjectList(clockJson.Replace("%time%", time)));
					}
				}
			}
			else
			{
				int size = BasePlayer.activePlayerList.Count;
				for(int i = 0; i < size; i++)
				{
					BasePlayer bp = BasePlayer.activePlayerList[i];
					if(!((bool)Config["PreventChangingTime"]))
						dt = gameTime;
					ShowTime();
					SendClientCommand(bp, "AddUI", new Facepunch.ObjectList(clockJson.Replace("%time%", time)));
				}
			}
		}

		private void UpdateTime()
		{
			gameTime = sky.Cycle.DateTime;
			serverTime = DateTime.Now;

			if((bool)Config["PreventChangingTime"])
				if((bool)Config["ServerTime"])
					dt = serverTime;
				else
					dt = gameTime;

			DestroyGUI();
			AddGUI();
			UpdateInfo();
		}

		private void DestroyGUI()
		{
			int size = BasePlayer.activePlayerList.Count;
			for(int i = 0; i < size; i++)
				SendClientCommand(BasePlayer.activePlayerList[i], "DestroyUI", new Facepunch.ObjectList("Clock"));
		}

		private void ShowTime()
		{
			if((int)Config["TimeFormat"] == 24)
				if((bool)Config["ShowSeconds"])
					time = dt.ToString("HH:mm:ss");
				else
					time = dt.ToString("HH:mm");
			else
				if((bool)Config["ShowSeconds"])
					time = dt.ToString("h:mm:ss tt");
				else
					time = dt.ToString("h:mm tt");
		}

		void ShowInfo(string text, string iSize)
		{
			int size = BasePlayer.activePlayerList.Count;
			for(int i = 0; i < size; i++)
				SendClientCommand(BasePlayer.activePlayerList[i], "AddUI", new Facepunch.ObjectList(infoJson.Replace("%info%", text).Replace("%info_right%", iSize)));
		}

		void UpdateInfo()
		{
			if(tiList.Count > 0)
			{
				DateTime g = DateTime.Parse(gameTime.ToString("HH:mm"));
				DateTime s = DateTime.Parse(serverTime.ToString("HH:mm"));

				if(currentTI == null)
				{
					for(int i = 0; i < tiList.Count; i++)
					{
						if(!tiList[i].serverTime)
						{
							if(g.Ticks > tiList[i].startTime && g.Ticks < tiList[i].endTime)
							{
								currentTI = tiList[i];
								ShowInfo(tiList[i].text, tiList[i].size);
							}
						}
						else
						{
							if(s.Ticks > tiList[i].startTime && s.Ticks < tiList[i].endTime)
							{
								currentTI = tiList[i];
								ShowInfo(tiList[i].text, tiList[i].size);
							}
						}
					}
				}
				else
				{
					if(!currentTI.serverTime)
					{
						if(g.Ticks > currentTI.endTime)
						{
							currentTI = null;
							DestroyInfo();
						}
					}
					else
					{
						if(s.Ticks > currentTI.endTime)
						{
							currentTI = null;
							DestroyInfo();
						}
					}
				}
			}
		}

		void DestroyInfo()
		{
			int size = BasePlayer.activePlayerList.Count;
			for(int i = 0; i < size; i++)
				SendClientCommand(BasePlayer.activePlayerList[i], "DestroyUI", new Facepunch.ObjectList("ClockInfo"));
		}

		void SendHelpText(BasePlayer player)
		{
			PrintToChat(player, (string)Config["Messages", "Help"]);
		}

		void CheckConfig()
		{
			ConfigItem("UpdateTimeInSeconds", 2);
			ConfigItem("ShowSeconds", false);

			ConfigItem("BackgroundColor", "0.1 0.1 0.1 0.3");
			ConfigItem("TextColor", "1 1 1 0.3");
			ConfigItem("FontSize", 14);

			ConfigItem("Position", "Left", 0.01);
			ConfigItem("Position", "Bottom", 0.015);

			ConfigItem("Size", "Width", 0.05);
			ConfigItem("Size", "Height", 0.03);

			ConfigItem("ServerTime", false);
			ConfigItem("PreventChangingTime", false);
			ConfigItem("TimeFormat", 24);

			ConfigItem("Prefix", "");
			ConfigItem("Postfix", "");

			ConfigItem("TimedInfo", new string[] { "" });

			ConfigItem("Messages", "Enabled", "You have enabled clock");
			ConfigItem("Messages", "Disabled", "You have disabled clock");
			ConfigItem("Messages", "STEnabled", "Now your clock shows server time");
			ConfigItem("Messages", "STDisabled", "Now your clock shows ingame time");
			ConfigItem("Messages", "Help", "Clock:\n/clock - toggle clock\n/clock server - toggle server/ingame time");
			ConfigItem("Messages", "PreventChangeEnabled", "You can't choose between server or ingame time");

			SaveConfig();
		}

		// ----------------------------- UTILS -----------------------------
		// -----------------------------------------------------------------

		private void ConfigItem(string name, object defaultValue)
		{
			Config[name] = Config[name] ?? defaultValue;
		}

		private void ConfigItem(string name1, string name2, object defaultValue)
		{
			Config[name1, name2] = Config[name1, name2] ?? defaultValue;
		}

		void SaveData()
		{
			Interface.GetMod().DataFileSystem.WriteObject(Title, data);
		}

		bool GetOption(int options, int option)
		{
			if((options & option) != 0)
				return true;
			else
				return false;
		}

		void SendClientCommand(BasePlayer player, string functionName, Facepunch.ObjectList arguments)
		{
			CommunityEntity.ServerInstance.ClientRPCEx(new Network.SendInfo(player.net.connection),	null, functionName, arguments);
		}

		private enum TIStates { Init, StartBracket, StartTime, FirstColon, SecondColon, Hyphen, EndTime, AfterBracket, Text, Size, SizeAfterDot };

		private TimedInfo GetTimedInfo(string source)
		{
			source = source.TrimStart().TrimEnd();

			TIStates currentState = TIStates.Init;
			string   startTime = "", endTime = "", text = "", nSize = "";
			bool 	 st = false; // Server time

			int size = source.Length;

			for(int i = 0; i < size; i++)
			{				
				switch(currentState)
				{
					case TIStates.Init:
						{
							if (source[i] == '[')
							{
								currentState = TIStates.StartTime;
							}
							else if(source[i] == 's' || source[i] == 'S')
							{
								st = true;
								i++;
								currentState = TIStates.StartTime;
							}
							break;
						}
					case TIStates.StartTime:
						{
							if (Char.IsDigit(source[i]))
							{
								startTime += source[i];
							}
							else if (source[i] == ':')
							{
								startTime += source[i];
								currentState = TIStates.FirstColon;
							}
							break;
						}
					case TIStates.FirstColon:
						{
							if (Char.IsDigit(source[i]))
							{
								startTime += source[i];
							}
							else if(source[i] == '-')
							{
								i--;
								currentState = TIStates.Hyphen;
							}
							break;
						}
					case TIStates.Hyphen:
						{
							if (Char.IsDigit(source[i]))
							{
								i--;
								currentState = TIStates.EndTime;
							}
							break;
						}
					case TIStates.EndTime:
						{
							if (Char.IsDigit(source[i]))
							{
								endTime += source[i];
							}
							else if (source[i] == ':')
							{
								endTime += source[i];
								currentState = TIStates.SecondColon;
							}
							break;
						}
					case TIStates.SecondColon:
						{
							if (Char.IsDigit(source[i]))
								endTime += source[i];
							else if(source[i] == ']')
								currentState = TIStates.AfterBracket;
							else if(source[i] == '-')
								currentState = TIStates.Size;
							break;
						}
					case TIStates.Size:
						{
							if(Char.IsDigit(source[i]))
							{
								nSize += source[i];
							}
							else if(source[i] == '.')
							{
								nSize += source[i];
								currentState = TIStates.SizeAfterDot;
							}
							break;
						}
					case TIStates.SizeAfterDot:
						{
							if(Char.IsDigit(source[i]))
								nSize += source[i];
							else if(source[i] == ']')
								currentState = TIStates.AfterBracket;
							break;
						}
					case TIStates.AfterBracket:
						{
							if(source[i] != ' ')
								text += source[i];
							currentState = TIStates.Text;
							break;
						}
					case TIStates.Text:
						{
							text += source[i];
							break;
						}
				}
			}

			if(nSize.Length == 0)
				nSize = defaultInfoSize;

			return new TimedInfo(DateTime.Parse(startTime).Ticks, DateTime.Parse(endTime).Ticks, text, st, nSize);
		}

	}
}