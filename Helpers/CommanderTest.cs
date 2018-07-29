using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Oxide.Core;
using Oxide.Core.Configuration;

namespace Oxide.Plugins
{
	[Info("Testing Commander", "deer_SWAG", "1.0.0", ResourceId = 0)]
	[Description("Tests stuff")]
	class CommanderTest : RustPlugin
	{
		Commander cmdr;

		void Init()
		{
			cmdr = new Commander();
			cmdr.Add(null, null, (player, args) =>
			{
				Puts("ROOT yey");
			});
			cmdr.Add("add", null, (player, args) =>
			{
				foreach (var item in args)
				{
					Puts("{0}: {1}", item.Key, item.Value);
				}
			}).AddParam("name", Commander.ParamType.String);

			var group = cmdr.AddGroup("find");
				group.Add(null, null, (player, args) =>
				{
					Puts("a group help text");
				});
			var group2 = group.AddGroup("player");
				group2.Add("name", null, (p, a) =>
				{
					Puts("Hello from group2");
				});
		}

		[ChatCommand("commander")]
		void cmdChat(BasePlayer player, string command, string[] args)
		{
			cmdr.Run(player, args);
		}

		[ConsoleCommand("test.commander")]
		void conCmd(ConsoleSystem.Arg args)
		{
			cmdr.Run(null, args.Args);
		}

		/* TODO
				- Add permission checking
				- (?) Make code smaller
				- Make required/not required parameters
		*/

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
				Int, Float,	Bool,
				/// <summary>Returns a BasePlayer object</summary>
				Player
			}

			public class Param
			{
				public string Name    { get; internal set; }
				public ParamType Type { get; internal set; }
				public bool Greedy    { get; internal set; }
			}

			public class ValueCollection : Dictionary<string, object>
			{
				public new object this[string key]
				{
					get
					{
						object value;
						this.TryGetValue(key, out value);
						return value;
					}
				}
				
				/// <exception cref="InvalidCastException"></exception>
				public T Get<T>(string key)
				{
					object value;
					this.TryGetValue(key, out value);
					return (T)value;
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
						Greedy =  greedy
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
					for(int i = 0; i < args.Length; i++)
					{
						if (i >= Params.Count)
							break;

						var arg = args[i];
						var param = Params[i];

						if (param.Greedy)
						{
							collection.Add(param.Name, string.Join(" ", args, i, args.Length - i));
							break;
						}

						collection.Add(param.Name, CheckAndConvert(arg, param.Type));
					}
				}

				private object CheckAndConvert(string arg, ParamType type)
				{
					switch (type)
					{
						case ParamType.String:
							return arg;
						case ParamType.Int:
							{
								int ret;
								if (int.TryParse(arg, out ret))
									return ret;
							}
							break;
						case ParamType.Float:
							{
								float ret;
								if (float.TryParse(arg, out ret))
									return ret;
							}
							break;
						case ParamType.Bool:
							{
								bool ret;
								if (ParseBool(arg, out ret))
									return ret;
							}
							break;
						case ParamType.Player:
							return BasePlayer.Find(arg);
					}

					return null;
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

			private static string[] _trueBoolValues  = { "true", "yes", "on", "enable", "1" };
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
	}
}
