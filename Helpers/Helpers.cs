using Oxide.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Oxide.Plugins
{
	class Helpers : RustPlugin
	{
		void ConfigItem(string name, object defaultValue)
		{
			Config[name] = Config[name] ?? defaultValue;
		}

		void ConfigItem(string name, string name2, object defaultValue)
		{
			Config[name, name2] = Config[name, name2] ?? defaultValue;
		}

		bool IsPluginExists(string name)
		{
			return Interface.Oxide.GetLibrary<Core.Libraries.Plugins>().Exists(name);
		}

		bool StringContains(string source, string value, StringComparison comparison)
		{
			return source.IndexOf(value, comparison) >= 0;
		}

		string Lang(string key, BasePlayer player = null)
		{
			return lang.GetMessage(key, this, player?.UserIDString);
		}

		bool PlayerHasPermission(BasePlayer player, string permissionName)
		{
			return player.IsAdmin || permission.UserHasPermission(player.UserIDString, permissionName);
		}
	}
}
