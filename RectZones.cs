using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Oxide.Core;
using Oxide.Core.Plugins;

using UnityEngine;

using Newtonsoft.Json;

/*
	TODO:
			- Make showing a zone better
			- Fix "nobuildex"
			- add deployables protection
			- what to do with ddraw?
*/

/* --------------------------------------------------------------------- */
/* --- Don't edit anything here if you don't know what you are doing --- */
/* --------------------------------------------------------------------- */

namespace Oxide.Plugins
{
	[Info("RectZones", "deer_SWAG", "0.1.0", ResourceId = 2029)]
	[Description("Creates polygonal zones")]
	public class RectZones : RustPlugin
	{
		#region Variables

		const int MaxPoints = 254;
		const int MinPoints = 6;
		const string PluginPermission = "rectzones.use";
		const string GameObjectPrefix = "rzone-";

		#endregion

		#region Data Classes

		class PluginData
		{
			[JsonProperty("zones")]
			public HashSet<ZoneDefinition> Zones = new HashSet<ZoneDefinition>();
		}

		class ZoneDefinition
		{
			[JsonProperty("id")]
			public string Id = string.Empty;
			[JsonProperty("n")]
			public string Name;
			[JsonProperty("v")]
			public List<JVector3> Vertices = new List<JVector3>();
			[JsonProperty("o")]
			public Dictionary<string, string> Options = new Dictionary<string, string>();
		}

		#endregion

		PluginData data;
		HashSet<GameObject> currentZones = new HashSet<GameObject>();

		class TemporaryStorage
		{
			public BasePlayer Player;
			public ZoneDefinition Zone;
			public Timer Timer;
			public bool Fixed;
			public float Height;
		}

		bool isEditing;
		List<TemporaryStorage> tempPlayers = new List<TemporaryStorage>();

		Dictionary<string, string> availableOptions = new Dictionary<string, string>()
		{
			{ "entermsg", "Shows message when a player enters a zone" },
			{ "exitmsg", "Shows message when a player exits a zone" },
			{ "nobuild", "Players can't build in zone" },
			//{ "nobuildex", "Players can't build in zone. All buildings in a zone will be demolished" },
			{ "nostability", "Removes stability from buildings" },
			{ "nodestroy", "Players will be unable to destroy buildings and deployables" },
			{ "nopvp", "Players won't get hurt by another player" }
		};

		protected override void LoadDefaultMessages()
		{
			lang.RegisterMessages(new Dictionary<string, string>()
			{
				{ "HelpText", "Rect Zones:\n/rect add <height> [fixed] [name]\n/rect finish\n/rect undo\n/rect remove <id/name>\n/rect list\n/rect options\n/rect show [zone id]\n/rect clear" },
				{ "AlreadyEditing", "You are editing a zone. Type /rect finish to finish editing" },
				{ "AddHelp", "Now you can start adding points by pressing \"USE\" button (default is \"E\"). They will be added where your crosshair is pointing. To add options type /options <name> = [value]. To finish type /rect finish" },
				{ "Done", "Zone <color=#ffff00ff>{name}</color> with id <color=#ffff00ff>{id}</color> was added" },
				{ "DonePointsCount", "Points count should be more than 2 and less than 128" },
				{ "RemoveHelp", "To remove a zone type /rect remove <id/name>" },
				{ "Removed", "Zone was removed" },
				{ "ZoneNotFound", "Zone was not found" },
				{ "Empty", "Empty. To add a zone type /rect add <height> [fixed]" },
				{ "OptionsHelp", "/rect options <zoneID>" },
				{ "NoOptions", "This zone has no options" },
				{ "Cleared", "All zones were removed" },
				{ "ShowEmpty", "Nothing to show" },
				{ "CurrentZone", "Currently you are in zone with ID {id}" },
				{ "CurrentZoneNoZone", "You are not in any zone" },
				{ "InvalidAddCommand", "/rect add <height> [fixed] [name]" },
				{ "HeightMustBeNumber", "Argument \"height\" must be a number. If you want to use floating point number then write it in the quotes" },
				{ "UndoComplete", "Last point was removed" },
				{ "List", "Zones:\n" }
			}, this);
		}

		Commander cmdr = new Commander();

		void Init()
		{
			LoadData();
			permission.RegisterPermission(PluginPermission, this);

			cmdr.Add(null, null, (player, args) =>
			{
				player.ChatMessage(Lang("HelpText", player));
			});

			cmdr.Add("add", null, (player, args) =>
			{
				if (CheckIsEditing(player) != null)
				{
					player.ChatMessage(Lang("AlreadyEditing", player));
					return;
				}

				if (args["height"].Value == null)
				{
					player.ChatMessage(Lang("InvalidAddCommand", player));
					return;
				}

				if (args["height"].IsInvalid)
				{
					player.ChatMessage(Lang("HeightMustBeNumber", player));
					return;
				}

				if (args.Count == 1)
				{
					CommandAdd(player, args["height"].Get<float>());
					return;
				}

				if (args["fixed"].String == "fixed")
				{
					if (args["name"].Value == null)
						CommandAdd(player, args["height"].Single, null, true);
					else
						CommandAdd(player, args["height"].Single, args["name"].String, true);
				}
				else
				{
					CommandAdd(player, args["height"].Single, args["fixed"].String);
				}

			}).AddParam("height", Commander.ParamType.Float)
			  .AddParam("fixed")
			  .AddParam("name");

			cmdr.Add("finish", null, (player, args) =>
			{
				TemporaryStorage storage = CheckIsEditing(player);

				if (storage != null)
				{
					if (storage.Zone.Vertices.Count < MinPoints || storage.Zone.Vertices.Count > MaxPoints)
					{
						player.ChatMessage(Lang("DonePointsCount", player));
						return;
					}

					CommandFinish(player);
					player.ChatMessage(Lang("Done", player).Replace("{id}", storage.Zone.Id).Replace("{name}", storage.Zone.Name));
				}
			});

			cmdr.Add("undo", null, (player, args) =>
			{
				TemporaryStorage storage = CheckIsEditing(player);

				if (storage != null)
				{
					if (storage.Zone.Vertices.Count != 0)
					{
						storage.Zone.Vertices.RemoveRange(storage.Zone.Vertices.Count - 2, 2);
						player.ChatMessage(Lang("UndoComplete", player));
					}
				}
			});

			cmdr.Add("list", null, (player, args) =>
			{
				if (data.Zones.Count == 0)
				{
					player.ChatMessage(Lang("Empty", player));
					return;
				}

				var result = new StringBuilder(Lang("List", player).Length);
				result.Append(Lang("List", player));

				foreach (ZoneDefinition zd in data.Zones)
					result.Append(zd.Id + (zd.Name != null ? (" (" + zd.Name + ")") : "") + ", ");
				
				player.ChatMessage(result.Remove(result.Length - 2, 2).ToString());
			});

			cmdr.Add("current", null, (player, args) =>
			{
				RectZone zone = GetCurrentZoneForPlayer(player);

				if (zone != null)
					player.ChatMessage(Lang("CurrentZone", player).Replace("{id}", zone.Definition.Id));
				else
					player.ChatMessage(Lang("CurrentZoneNoZone", player));
			});

			cmdr.Add("clear", null, (player, args) =>
			{
				foreach (GameObject zone in currentZones)
					GameObject.Destroy(zone);

				currentZones.Clear();
				data.Zones.Clear();

				player.ChatMessage(Lang("Cleared", player));
			});

			cmdr.Add("show", null, (player, args) => // TODO show by name
			{
				if (args["id"].Value != null)
				{
					if (args["id"].String.Equals("current", StringComparison.CurrentCultureIgnoreCase))
					{
						RectZone zone = GetCurrentZoneForPlayer(player);
						zone.ShowZone(player, 10f);
					}
					else
					{
						var zone = currentZones.First((z) => z.GetComponent<RectZone>().Definition.Id.Equals(args["id"].String));

						if (zone != null)
							zone.GetComponent<RectZone>().ShowZone(player, 10f);
						else
							player.ChatMessage(Lang("ZoneNotFound", player));						
					}
				}
				else
				{
					if (currentZones.Count == 0)
					{
						player.ChatMessage(Lang("ShowEmpty", player));
						return;
					}

					foreach (GameObject zone in currentZones)
						zone.GetComponent<RectZone>().ShowZone(player, 10f);
				}

			}).AddParam("id");

			cmdr.Add("remove", null, (player, args) =>
			{
				if (CheckIsEditing(player) != null)
				{
					player.ChatMessage(Lang("AlreadyEditing", player));
					return;
				}

				if (args["nameOrId"].Value != null)
				{
					if (IsDigitsOnly(args["nameOrId"].String))
					{
						int removed = data.Zones.RemoveWhere(x => x.Id == args["nameOrId"].String);

						if (removed <= 0)
						{
							player.ChatMessage(Lang("ZoneNotFound", player));
							return;
						}

						currentZones.RemoveWhere((GameObject go) =>
						{
							RectZone rz = go.GetComponent<RectZone>();

							if (rz.Definition.Id == args["nameOrId"].String)
							{
								GameObject.Destroy(go);
								return true;
							}

							return false;
						});

						player.ChatMessage(Lang("Removed", player));
					}
					else
					{
						int removed = data.Zones.RemoveWhere((ZoneDefinition x) =>
						{
							if (x.Name == null)
								return false;

							return x.Name.Equals(args["nameOrId"].String, StringComparison.CurrentCultureIgnoreCase);
						});

						if (removed <= 0)
						{
							player.ChatMessage(Lang("ZoneNotFound", player));
							return;
						}

						currentZones.RemoveWhere((GameObject go) =>
						{
							RectZone rz = go.GetComponent<RectZone>();

							if (rz.Definition.Name.Equals(args["nameOrId"].String, StringComparison.CurrentCultureIgnoreCase))
							{
								GameObject.Destroy(go);
								return true;
							}

							return false;
						});

						player.ChatMessage(Lang("Removed", player));
					}
				}
				else
				{
					player.ChatMessage(Lang("RemoveHelp", player));
				}

			}).AddParam("nameOrId");

			cmdr.Add("options", null, (player, args) =>
			{
				TemporaryStorage storage = CheckIsEditing(player);

				if (storage == null)
				{
					if (args["command"].Value != null)
					{
						if (IsDigitsOnly(args["command"].String))
						{
							ZoneDefinition definition = null;

							foreach (ZoneDefinition d in data.Zones)
							{
								if (d.Id == args["command"].String)
								{
									definition = d;
									break;
								}
							}

							if (definition == null)
							{
								player.ChatMessage(Lang("ZoneNotFound", player));
								return;
							}

							if (definition.Options.Count == 0)
							{
								player.ChatMessage(Lang("NoOptions", player));
								return;
							}

							string result = string.Empty;
							foreach (KeyValuePair<string, string> option in definition.Options)
							{
								result += option.Key + (option.Value != null ? (" = " + option.Value) : "") + ", ";
							}

							player.ChatMessage(result.Substring(0, result.Length - 2));
						}
						else
						{
							player.ChatMessage(Lang("OptionsHelp", player));
						}
					}
					else
					{
						string result = string.Empty;

						foreach (KeyValuePair<string, string> option in availableOptions)
						{
							result += "<color=#ffa500ff>" + option.Key + "</color> - " + (option.Value ?? "No description") + "\n";
						}

						player.ChatMessage(result);
					}
				}
				else
				{
					if (args["command"].Value != null)
					{
						if (args["command"].String == "list")
						{
							string result = string.Empty;

							foreach (KeyValuePair<string, string> option in availableOptions)
							{
								result += "<color=#ffa500ff>" + option.Key + "</color> - " + (option.Value ?? "") + "\n";
							}

							player.ChatMessage(result);

							return;
						}

						string[] opts = args["command"].String.Split(' ');

						for (int i = 1; i < opts.Length; i++)
						{
							string[] option = opts[i].Split(new char[] { '=' }, 2);

							storage.Zone.Options.Add(option[0], (option.Length > 1 ? option[1] : null));
						}
					}
					else
					{
						if (storage.Zone.Options.Count != 0)
						{
							string result = string.Empty;
							foreach (KeyValuePair<string, string> option in storage.Zone.Options)
							{
								result += option.Key + " = " + option.Value + ", \n";
							}

							player.ChatMessage(result.Substring(0, result.Length - 2));
						}
						else
						{
							player.ChatMessage(Lang("NoOptions", player));
						}
					}
				}
			}).AddParam("command", Commander.ParamType.String, true);
		}

		void Unload()
		{
			foreach(GameObject zone in currentZones)
				GameObject.Destroy(zone);

			currentZones.Clear();
		}

		void OnServerInitialized()
		{
			foreach (ZoneDefinition definition in data.Zones)
				CreateZoneByDefinition(definition);

			Puts("{0} zones were created", data.Zones.Count);
		}

		void OnServerSave()
		{
			SaveData();
		}

		[ChatCommand("rect")]
		void cmdChat(BasePlayer player, string command, string[] args)
		{
			//if (!PlayerHasPermission(player, PluginPermission))
			//return;

			cmdr.Run(player, args);
		}

		#region Point Addition

		void OnPlayerDisconnected(BasePlayer player, string reason)
		{
			foreach(GameObject zone in currentZones)
			{
				RectZone zoneComponent = zone.GetComponent<RectZone>();
				zoneComponent.Players.Remove(player);
			}

			if (isEditing)
				tempPlayers.RemoveAll(x => x.Player.userID == player.userID);
		}

		void OnPlayerInput(BasePlayer player, InputState input)
		{
			if (!isEditing)
				return;

			if (!input.WasJustPressed(BUTTON.USE))
				return;
	
			for (int i = 0; i < tempPlayers.Count; i++)
			{
				TemporaryStorage storage = tempPlayers[i];

				if (storage.Player.userID == player.userID)
				{
					Ray ray = player.eyes.HeadRay();
					RaycastHit hit;

					if(Physics.Raycast(ray, out hit, 10))
					{
						JVector3 bottomPoint = new JVector3(hit.point);
						JVector3 topPoint = storage.Fixed ? new JVector3(hit.point.x, storage.Height, hit.point.z) : new JVector3(hit.point.x, hit.point.y + storage.Height, hit.point.z);

						storage.Zone.Vertices.Add(bottomPoint);
						storage.Zone.Vertices.Add(topPoint);

						ShowPoint(player, bottomPoint.ToUnity(), topPoint.ToUnity(), 2f);
					}
				}
			}
		}

		#endregion Point Addition

		#region Commands

		TemporaryStorage CheckIsEditing(BasePlayer player)
		{
			if (tempPlayers.Count == 0)
				return null;

			return tempPlayers.Find(x => x.Player.userID == player.userID);
		}

		void CommandAdd(BasePlayer player, float height, string name = null, bool fixedHeight = false)
		{
			player.ChatMessage(Lang("AddHelp", player));

			isEditing = true;

			TemporaryStorage storage = new TemporaryStorage();
			storage.Player = player;
			storage.Zone = new ZoneDefinition
			{
				Id = GenerateId()
			};
			storage.Height = height;
			storage.Fixed = fixedHeight;
			storage.Zone.Name = name;

			storage.Timer = timer.Repeat(5f, 0, () =>
			{
				JVector3 prevVector = null;

				foreach(JVector3 vector in storage.Zone.Vertices)
				{
					if(prevVector == null)
					{
						prevVector = vector;
						continue;
					}

					ShowPoint(player, prevVector.ToUnity(), vector.ToUnity(), 5.5f);

					prevVector = null;
				}
			});

			tempPlayers.Add(storage);
		}

		void CommandFinish(BasePlayer player)
		{
			tempPlayers.RemoveAll((TemporaryStorage storage) =>
			{
				if(storage.Player.userID == player.userID)
				{
					storage.Timer.Destroy();
					storage.Timer = null;
					storage.Player = null;

					data.Zones.Add(storage.Zone);

					CreateZoneByDefinition(storage.Zone);

					return true;
				}

				return false;
			});

			SaveData();

			if(tempPlayers.Count == 0)
				isEditing = false;
		}

		void CreateZoneByDefinition(ZoneDefinition definition)
		{
			GameObject zoneObject = new GameObject(GameObjectPrefix + definition.Id);
			RectZone zoneComponent = zoneObject.AddComponent<RectZone>();
			zoneComponent.SetZone(definition);
			currentZones.Add(zoneObject);
		}

		RectZone GetCurrentZoneForPlayer(BasePlayer player)
		{
			foreach (GameObject zone in currentZones)
			{
				RectZone zoneComponent = zone.GetComponent<RectZone>();

				if (zoneComponent.Players.Contains(player))
				{
					return zoneComponent;
				}
			}

			return null;
		}

		#endregion Commands

		// For HelpText plugin

		void SendHelpText(BasePlayer player)
		{
			if(PlayerHasPermission(player, PluginPermission))
				player.ChatMessage(Lang("HelpText", player));
		}

		#region For Built-in Options

		void OnEntityBuilt(Planner plan, GameObject go)
		{
			foreach (GameObject zone in currentZones)
			{
				RectZone zoneComponent = zone.GetComponent<RectZone>();

				if (zoneComponent.Players.Count == 0)
					continue;

				if (zoneComponent.Definition.Options.ContainsKey("nobuild"))
				{
					if (zoneComponent.GetComponent<Collider>().bounds.Contains(go.transform.position))
					{
						go.GetComponentInParent<BaseCombatEntity>().Kill(BaseNetworkable.DestroyMode.Gib);

						if (zoneComponent.Definition.Options["nobuild"] != null)
							plan.GetOwnerPlayer().ChatMessage(zoneComponent.Definition.Options["nobuild"]);

						break;
					}
				}
				else if(zoneComponent.Definition.Options.ContainsKey("nobuildex"))
				{
					if (zoneComponent.GetComponent<Collider>().bounds.Contains(go.transform.position))
					{
						go.GetComponentInParent<BaseCombatEntity>().Kill(BaseNetworkable.DestroyMode.Gib);

						if (zoneComponent.Definition.Options["nobuildex"] != null)
							plan.GetOwnerPlayer().ChatMessage(zoneComponent.Definition.Options["nobuildex"]);

						break;
					}
				}
			}
		}

		void OnEntitySpawned(BaseNetworkable entity)
		{
			if (!(entity is BuildingBlock))
				return;

			BuildingBlock block = (BuildingBlock)entity;

			foreach(GameObject zone in currentZones)
			{
				RectZone zoneComponent = zone.GetComponent<RectZone>();

				if (zoneComponent.Definition.Options.ContainsKey("nostability"))
				{
					if (zoneComponent.GetComponent<Collider>().bounds.Contains(block.transform.position))
					{
						block.grounded = true;
					}
				}
			}
		}

		void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo hitinfo)
		{
			bool blockDamage = false;

			foreach (GameObject zone in currentZones)
			{
				RectZone zoneComponent = zone.GetComponent<RectZone>();

				if((entity is BuildingBlock && zoneComponent.Definition.Options.ContainsKey("nodestroy")) ||
				   (entity is BasePlayer && (hitinfo.Initiator is BasePlayer || hitinfo.Initiator is FireBall) && zoneComponent.Definition.Options.ContainsKey("nopvp")))
				{
					if(zoneComponent.GetComponent<Collider>().bounds.Contains(entity.transform.position))
					{
						blockDamage = true;
						break;
					}
				}
			}

			if(blockDamage)
			{
				hitinfo.damageTypes = new Rust.DamageTypeList();
				hitinfo.DoHitEffects = false;
				hitinfo.HitMaterial = 0;
			}
		}

		#endregion Options

		#region Hooks For Other Plugins

		[HookMethod("RegisterOption")]
		public bool RegisterOption(string name, string description = null)
		{
			if(availableOptions.Count > 0)
			{
				if (availableOptions.ContainsKey(name))
				{
					PrintWarning("There is already an option with name \"" + name + "\"");
					return false;
				}
			}

			availableOptions.Add(name, description);

			return true;
		}

		[HookMethod("UnregisterOption")]
		public void UnregisterOption(string name)
		{
			availableOptions.Remove(name);
		}

		// Hooks
		// void OnEnterZone(string id, BasePlayer player)
		// void OnExitZone(string id, BasePlayer player)
		// void OnEnterZoneWithOptions(string id, BasePlayer player, Dictionary<string, string> options)
		// void OnExitZoneWithOptions(string id, BasePlayer player, Dictionary<string, string> options)

		#endregion Hooks

		#region Utils

		static void ShowPoint(BasePlayer player, Vector3 from, Vector3 to, float duration = 5f)
		{
			DrawDebugLine(player, from, to, 4.8f);
			DrawDebugSphere(player, from, 0.1f, 4.8f);
			DrawDebugSphere(player, to, 0.1f, 4.8f);
		}

		void LoadData()
		{
			data = Interface.GetMod().DataFileSystem.ReadObject<PluginData>(Title);
		}

		void SaveData()
		{
			Interface.GetMod().DataFileSystem.WriteObject(Title, data);
		}

		static void DrawDebugLine(BasePlayer player, Vector3 from, Vector3 to, float duration = 1f)
		{
			player.SendConsoleCommand("ddraw.line", duration, Color.yellow, from, to);
		}

		static void DrawDebugSphere(BasePlayer player, Vector3 position, float radius = 0.5f, float duration = 1f)
		{
			player.SendConsoleCommand("ddraw.sphere", duration, Color.green, position, radius);
		}

		string GenerateId()
		{
			byte[] bytes = Guid.NewGuid().ToByteArray();
			int number = Math.Abs(BitConverter.ToInt32(bytes, 0));
			return number.ToString();
		}

		string Lang(string key, BasePlayer player = null)
		{
			return lang.GetMessage(key, this, player?.UserIDString);
		}

		bool IsDigitsOnly(string str)
		{
			foreach (char c in str)
				if (!char.IsDigit(c))
					return false;
			return true;
		}

		bool PlayerHasPermission(BasePlayer player, string permissionName)
		{
			return player.IsAdmin || permission.UserHasPermission(player.UserIDString, permissionName);
		}

		#endregion Utils

		#region Helpers

		/// <summary>
		/// Helper class for chat and console commands
		/// </summary>
		public class Commander
		{
			#region Definitions

			/// <summary>Type of parameter for auto convertion</summary>
			public enum ParamType
			{
				/// <summary>Use this if you want a raw string</summary>
				String,
				Int, Float, Bool,
				/// <summary>Returns a BasePlayer object</summary>
				Player
			}

			public struct Param
			{
				public string Name { get; internal set; }
				public ParamType Type { get; internal set; }
				public bool Greedy { get; internal set; }
			}

			public class ParamValue
			{
				/// <summary>Raw value</summary>
				public object Value { get; internal set; }
				/// <summary>If argument wasn't parse properly</summary>
				public bool IsInvalid { get; internal set; }

				public string String => Get<string>();
				public int Integer   => Get<int>();
				public float Single  => Get<float>();
				public BasePlayer Player => Get<BasePlayer>();
				
				/// <exception cref="InvalidCastException"></exception>
				public T Get<T>()
				{
					return (T)Value;
				}
			}

			public class ValueCollection : Dictionary<string, ParamValue>
			{
				public new ParamValue this[string key]
				{
					get
					{
						ParamValue value;
						this.TryGetValue(key, out value);
						return value;
					}
				}
			}

			public class BaseCommand
			{
				public string Name { get; internal set; }
				public string Permission { get; internal set; }

				public virtual void Run(BasePlayer player, string[] args) { }
			}

			public class Command : BaseCommand
			{
				public List<Param> Params { get; internal set; } = new List<Param>();
				public Action<BasePlayer, ValueCollection> Callback { get; internal set; }

				public Command AddParam(string name, ParamType type = ParamType.String, bool greedy = false)
				{
					if (name == null)
						throw new NullReferenceException("Name of the parameter can't be null");
					if (greedy && type != ParamType.String)
						throw new InvalidOperationException("Only string-type parameters can be greedy");

					Params.Add(new Param
					{
						Name = name,
						Type = type,
						Greedy = greedy
					});

					return this;
				}

				public override void Run(BasePlayer player, string[] args)
				{
					var collection = new ValueCollection();

					if (Params.Count > 0)
						CheckParams(args, ref collection);

					Callback?.Invoke(player, collection);
				}

				private void CheckParams(string[] args, ref ValueCollection collection)
				{
					for (int i = 0; i < args.Length; i++)
					{
						if (i >= Params.Count)
							break;

						var arg = args[i];
						var param = Params[i];

						if (param.Greedy)
						{
							collection.Add(param.Name, new ParamValue { Value = string.Join(" ", args, i, args.Length - i) });
							break;
						}

						object value;
						bool result = CheckAndConvert(arg, param.Type, out value);

						collection.Add(param.Name, new ParamValue { Value = value, IsInvalid = !result });
					}
				}

				private bool CheckAndConvert(string arg, ParamType type, out object result)
				{
					switch (type)
					{
						case ParamType.String:
							result = arg;
							return true;
						case ParamType.Int:
							{
								int ret;
								if (int.TryParse(arg, out ret))
								{
									result = ret;
									return true;
								}
								result = ret;
								return false;
							}
						case ParamType.Float:
							{
								float ret;
								if (float.TryParse(arg, out ret))
								{
									result = ret;
									return true;
								}
								result = ret;
								return false;
							}
						case ParamType.Bool:
							{
								bool ret;
								if (ParseBool(arg, out ret))
								{
									result = ret;
									return true;
								}
								result = ret;
								return false;
							}
						case ParamType.Player:
							{
								result = BasePlayer.Find(arg);
								return true;
							}
					}

					result = null;
					return false;
				}
			}

			public class CommandGroup : BaseCommand
			{
				private List<BaseCommand> _children = new List<BaseCommand>();
				private Command _empty;

				public Command Add(string name, string permission, Action<BasePlayer, ValueCollection> callback)
				{
					var cmd = new Command
					{
						Name = name,
						Permission = permission,
						Callback = callback
					};

					if (name == null)
						return _empty = cmd;

					_children.Add(cmd);
					return cmd;
				}

				public CommandGroup AddGroup(string name, string permission = null)
				{
					var group = new CommandGroup
					{
						Name = name,
						Permission = permission
					};

					_children.Add(group);
					return group;
				}

				public override void Run(BasePlayer player, string[] args)
				{
					Commander.Run(_children, _empty, player, args);
				}
			}

			#endregion Definitions

			private List<BaseCommand> _commands = new List<BaseCommand>();
			private Command _empty;

			public Command Add(string name, string permission, Action<BasePlayer, ValueCollection> callback)
			{
				var cmd = new Command
				{
					Name = name,
					Permission = permission,
					Callback = callback
				};

				if (name == null || name.Length == 0)
					return _empty = cmd;

				_commands.Add(cmd);
				return cmd;
			}

			public CommandGroup AddGroup(string name, string permission = null)
			{
				var group = new CommandGroup
				{
					Name = name,
					Permission = permission
				};

				_commands.Add(group);
				return group;
			}

			static protected void Run(List<BaseCommand> cmds, BaseCommand empty, BasePlayer player, string[] args)
			{
				if (args == null || args.Length == 0)
				{
					empty?.Run(player, args);
					return;
				}

				cmds.Find((cmd) => cmd.Name.Equals(args[0], StringComparison.CurrentCultureIgnoreCase))
				   ?.Run(player, args.Skip(1).ToArray());
			}

			public void Run(BasePlayer player, string[] args)
			{
				Run(_commands, _empty, player, args);
			}

			#region Custom Boolean Parser

			private static string[] _trueBoolValues = { "true", "yes", "on", "enable", "1" };
			private static string[] _falseBoolValues = { "false", "no", "off", "disable", "0" };

			protected static bool ParseBool(string input, out bool result)
			{
				for (int i = 0; i < _trueBoolValues.Length; i++)
				{
					if (input.Equals(_trueBoolValues[i], StringComparison.CurrentCultureIgnoreCase))
					{
						return result = true;
					}
					else if (input.Equals(_falseBoolValues[i], StringComparison.CurrentCultureIgnoreCase))
					{
						result = false;
						return true;
					}
				}

				return result = false;
			}

			#endregion Custom Boolean Parser
		}

		class JVector3
		{
			public float x;
			public float y;
			public float z;

			public JVector3(float x, float y, float z)
			{
				this.x = x;
				this.y = y;
				this.z = z;
			}

			public JVector3(Vector3 vector)
			{
				x = vector.x;
				y = vector.y;
				z = vector.z;
			}

			public Vector3 ToUnity()
			{
				return new Vector3(x, y, z);
			}
		}

		#endregion Helpers

		class RectZone : MonoBehaviour
		{
			public ZoneDefinition Definition { get; private set; }
			public HashSet<BasePlayer> Players { get; private set; } = new HashSet<BasePlayer>();

			private MeshCollider collider;

			void Awake()
			{
				gameObject.layer = (int)Rust.Layer.Reserved1;

				Rigidbody rigidbody = gameObject.AddComponent<Rigidbody>();
				rigidbody.isKinematic = true;
				rigidbody.useGravity = false;
				rigidbody.detectCollisions = true;

				collider = gameObject.AddComponent<MeshCollider>();
				collider.convex = true;
				collider.isTrigger = true;
			}

			public void SetZone(ZoneDefinition definition)
			{
				Definition = definition;
				MakeMesh(definition.Vertices);
			}

			public void ShowZone(BasePlayer player, float duration)
			{
				Vector3? prevVertex = null;

				foreach (Vector3 vertex in collider.sharedMesh.vertices)
				{
					if (prevVertex == null)
					{
						prevVertex = vertex;
						continue;
					}

					ShowPoint(player, prevVertex.Value, vertex, duration);

					prevVertex = null;
				}
			}

			void MakeMesh(List<JVector3> vertices)
			{
				Mesh mesh = new Mesh();

				List<Vector3> tempVertices = new List<Vector3>();
				List<int> tempIndices = new List<int>();

				foreach(JVector3 vertex in vertices)
					tempVertices.Add(vertex.ToUnity());

				// Side
				tempIndices.Add(0);
				tempIndices.Add(1);

				for (int i = 2; i < tempVertices.Count; i++)
				{
					tempIndices.Add(i);

					if (tempIndices.Count % 3 == 0)
					{
						tempIndices.Add(i - 1);
						tempIndices.Add(i + 1);
						tempIndices.Add(i);
						tempIndices.Add(i);
					}
				}

				tempIndices.Add(0);
				tempIndices.Add(tempVertices.Count - 1);
				tempIndices.Add(1);
				tempIndices.Add(0);

				mesh.SetVertices(tempVertices);
				mesh.SetIndices(tempIndices.ToArray(), MeshTopology.Triangles, 0);

				mesh.RecalculateBounds();
				mesh.RecalculateNormals();

				collider.sharedMesh = mesh;
			}
			
			void OnTriggerEnter(Collider collider)
			{
				BasePlayer player = collider.GetComponentInParent<BasePlayer>();

				if (player != null)
				{
					Players.Add(player);

					if (Definition.Options.ContainsKey("entermsg") && Definition.Options["entermsg"] != null)
						player.ChatMessage(Definition.Options["entermsg"]);

					Interface.Oxide.CallHook("OnEnterZone", Definition.Id, player);

					if(Definition.Options.Count != 0)
						Interface.Oxide.CallHook("OnEnterZoneWithOptions", Definition.Id, player, Definition.Options);
				}
				else
				{
					/*if (Definition.Options.ContainsKey("nobuildex"))
					{
						MeshColliderBatch batch = collider.GetComponent<MeshColliderBatch>();

						if(batch != null)
						{
							FieldInfo info = batch.GetType().GetField("instances", BindingFlags.NonPublic | BindingFlags.Instance);
							var batchColliders = (ListDictionary<Component, ColliderCombineInstance>)info.GetValue(batch);

							List<ColliderCombineInstance> batchCollidersList = batchColliders.Values;

							for(int i = 0; i < batchCollidersList.Count; i++)
							{
								ColliderCombineInstance instance = batchCollidersList[i];

								if(this.collider.bounds.Intersects(instance.bounds.ToBounds()))
								{
									instance.collider.GetComponentInParent<BaseCombatEntity>()?.Kill(BaseNetworkable.DestroyMode.Gib);
								}
							}
						}
					}*/
				}
			}

			void OnTriggerExit(Collider collider)
			{
				BasePlayer player = collider.GetComponentInParent<BasePlayer>();

				if (player != null)
				{
					Players.Remove(player);

					if (Definition.Options.ContainsKey("exitmsg") && Definition.Options["exitmsg"] != null)
						player.ChatMessage(Definition.Options["exitmsg"]);

					Interface.Oxide.CallHook("OnExitZone", Definition.Id, player);

					if(Definition.Options.Count != 0)
						Interface.Oxide.CallHook("OnExitZoneWithOptions", Definition.Id, player, Definition.Options);
				}
			}
		}
	}
}
