using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CS2_SimpleAdmin.Enums;
using CS2_SimpleAdmin.Managers;

namespace CS2_SimpleAdmin;

public partial class CS2_SimpleAdmin
{
	[ConsoleCommand("css_gag")]
	[RequiresPermissions("@css/chat")]
	[CommandHelper(minArgs: 1, usage: "<#userid or name> [time in minutes/0 perm] [reason]", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
	public void OnGagCommand(CCSPlayerController? caller, CommandInfo command)
	{
		if (Database == null) return;
		var callerName = caller == null ? "Console" : caller.PlayerName;

		var reason = _localizer?["sa_unknown"] ?? "Unknown";

		var targets = GetTarget(command);
		if (targets == null) return;
		var playersToTarget = targets.Players.Where(player => player is { IsValid: true, IsHLTV: false }).ToList();

		if (playersToTarget.Count > 1 && Config.OtherSettings.DisableDangerousCommands || playersToTarget.Count == 0)
		{
			return;
		}

		int.TryParse(command.GetArg(2), out var time);

		if (command.ArgCount >= 3 && command.GetArg(3).Length > 0)
			reason = command.GetArg(3);

		MuteManager muteManager = new(Database);

		playersToTarget.ForEach(player =>
		{
			if (caller!.CanTarget(player))
			{
				Gag(caller, player, time, reason, callerName, muteManager, command);
			}
		});
	}

	internal static void Gag(CCSPlayerController? caller, CCSPlayerController player, int time, string reason, string? callerName = null, MuteManager? muteManager = null, CommandInfo? command = null, bool silent = false)
	{
		if (Database == null || !player.IsValid || !player.UserId.HasValue) return;
		if (!caller.CanTarget(player)) return;

		// Set default caller name if not provided
		callerName ??= caller == null ? "Console" : caller.PlayerName;
		muteManager ??= new MuteManager(Database);

		// Get player and admin information
		var playerInfo = PlayersInfo[player.UserId.Value];
		var adminInfo = caller != null && caller.UserId.HasValue ? PlayersInfo[caller.UserId.Value] : null;

		// Asynchronously handle gag logic
		Task.Run(async () =>
		{
			await muteManager.MutePlayer(playerInfo, adminInfo, reason, time);
		});

		// Execute tag mute if needed
		if (TagsDetected)
		{
			Server.ExecuteCommand($"css_tag_mute {player.SteamID}");
		}

		// Add penalty to the player's penalty manager
		PlayerPenaltyManager.AddPenalty(player.Slot, PenaltyType.Gag, DateTime.Now.AddMinutes(time), time);

		// Determine message keys and arguments based on gag time (permanent or timed)
		var (messageKey, activityMessageKey, playerArgs, adminActivityArgs) = time == 0
			? ("sa_player_gag_message_perm", "sa_admin_gag_message_perm",
				[reason, "CALLER"],
				["CALLER", player.PlayerName, reason])
			: ("sa_player_gag_message_time", "sa_admin_gag_message_time", 
				new object[] { reason, time, "CALLER" },
				new object[] { "CALLER", player.PlayerName, reason, time });

		// Display center message to the gagged player
		Helper.DisplayCenterMessage(player, messageKey, callerName, playerArgs);

		// Display admin activity message to other players
		if (caller == null || !SilentPlayers.Contains(caller.Slot))
		{
			Helper.ShowAdminActivity(activityMessageKey, callerName, adminActivityArgs);
		}

		// Increment the player's total gags count
		PlayersInfo[player.UserId.Value].TotalGags++;

		// Log the gag command and send Discord notification
		if (!silent)
		{
			if (command == null)
				Helper.LogCommand(caller, $"css_gag {(string.IsNullOrEmpty(player.PlayerName) ? player.SteamID.ToString() : player.PlayerName)} {time} {reason}");
			else
				Helper.LogCommand(caller, command);
		}
		    
		Helper.SendDiscordPenaltyMessage(caller, player, reason, time, PenaltyType.Gag, _localizer);
	}

	[ConsoleCommand("css_addgag")]
	[RequiresPermissions("@css/chat")]
	[CommandHelper(minArgs: 1, usage: "<steamid> [time in minutes/0 perm] [reason]", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
	public void OnAddGagCommand(CCSPlayerController? caller, CommandInfo command)
	{
		if (Database == null) return;
		    
		// Set caller name
		var callerName = caller == null ? "Console" : caller.PlayerName;

		// Validate command arguments
		if (command.ArgCount < 2 || string.IsNullOrEmpty(command.GetArg(1))) return;

		// Validate and extract SteamID
		if (!Helper.ValidateSteamId(command.GetArg(1), out var steamId) || steamId == null)
		{
			command.ReplyToCommand("Invalid SteamID64.");
			return;
		}

		var steamid = steamId.SteamId64.ToString();
		var reason = command.ArgCount >= 3 && command.GetArg(3).Length > 0 
			? command.GetArg(3) 
			: (_localizer?["sa_unknown"] ?? "Unknown");

		MuteManager muteManager = new(Database);
		int.TryParse(command.GetArg(2), out var time);

		// Get player and admin info
		var adminInfo = caller != null && caller.UserId.HasValue ? PlayersInfo[caller.UserId.Value] : null;

		// Attempt to match player based on SteamID
		var matches = Helper.GetPlayerFromSteamid64(steamid);
		var player = matches.Count == 1 ? matches.FirstOrDefault() : null;

		if (player != null && player.IsValid)
		{
			// Check if caller can target the player
			if (!caller.CanTarget(player)) return;

			// Perform the gag for an online player
			Gag(caller, player, time, reason, callerName, muteManager, silent: true);
		}
		else
		{
			// Asynchronous gag operation for offline players
			Task.Run(async () =>
			{
				await muteManager.AddMuteBySteamid(steamid, adminInfo, reason, time);
			});

			command.ReplyToCommand($"Player with steamid {steamid} is not online. Gag has been added offline.");
		}
		    
		// Log the gag command and respond to the command
		Helper.LogCommand(caller, command);
	}

	[ConsoleCommand("css_ungag")]
	[RequiresPermissions("@css/chat")]
	[CommandHelper(minArgs: 1, usage: "<steamid or name> [reason]", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
	public void OnUngagCommand(CCSPlayerController? caller, CommandInfo command)
	{
		if (Database == null) return;

		var callerSteamId = caller?.SteamID.ToString() ?? "Console";
		var pattern = command.GetArg(1);
		var reason = command.GetArg(2);

		if (pattern.Length <= 1)
		{
			command.ReplyToCommand($"Too short pattern to search.");
			return;
		}

		Helper.LogCommand(caller, command);
		var muteManager = new MuteManager(Database);

		// Check if pattern is a valid SteamID64
		if (Helper.ValidateSteamId(pattern, out var steamId) && steamId != null)
		{
			var matches = Helper.GetPlayerFromSteamid64(steamId.SteamId64.ToString());
			var player = matches.Count == 1 ? matches.FirstOrDefault() : null;

			if (player != null && player.IsValid)
			{
				PlayerPenaltyManager.RemovePenaltiesByType(player.Slot, PenaltyType.Gag);

				if (TagsDetected)
					Server.ExecuteCommand($"css_tag_unmute {player.SteamID}");

				Task.Run(async () =>
				{
					await muteManager.UnmutePlayer(player.SteamID.ToString(), callerSteamId, reason);
				});

				command.ReplyToCommand($"Ungaged player {player.PlayerName}.");
				return;
			}
		}

		// If not a valid SteamID64, check by player name
		var nameMatches = Helper.GetPlayerFromName(pattern);
		var namePlayer = nameMatches.Count == 1 ? nameMatches.FirstOrDefault() : null;

		if (namePlayer != null && namePlayer.IsValid)
		{
			PlayerPenaltyManager.RemovePenaltiesByType(namePlayer.Slot, PenaltyType.Gag);

			if (namePlayer.UserId.HasValue && PlayersInfo[namePlayer.UserId.Value].TotalGags > 0) 
				PlayersInfo[namePlayer.UserId.Value].TotalGags--;

			if (TagsDetected)
				Server.ExecuteCommand($"css_tag_unmute {namePlayer.SteamID}");

			Task.Run(async () =>
			{
				await muteManager.UnmutePlayer(namePlayer.SteamID.ToString(), callerSteamId, reason);
			});

			command.ReplyToCommand($"Ungaged player {namePlayer.PlayerName}.");
		}
		else
		{
			Task.Run(async () =>
			{
				await muteManager.UnmutePlayer(pattern, callerSteamId, reason);
			});

			command.ReplyToCommand($"Ungaged offline player with pattern {pattern}.");
		}
	}

	[ConsoleCommand("css_mute")]
	[RequiresPermissions("@css/chat")]
	[CommandHelper(minArgs: 1, usage: "<#userid or name> [time in minutes/0 perm] [reason]", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
	public void OnMuteCommand(CCSPlayerController? caller, CommandInfo command)
	{
		if (Database == null) return;
		var callerName = caller == null ? "Console" : caller.PlayerName;

		var reason = _localizer?["sa_unknown"] ?? "Unknown";

		var targets = GetTarget(command);
		if (targets == null) return;
		var playersToTarget = targets.Players.Where(player => player is { IsValid: true, IsHLTV: false }).ToList();

		if (playersToTarget.Count > 1 && Config.OtherSettings.DisableDangerousCommands || playersToTarget.Count == 0)
		{
			return;
		}

		int.TryParse(command.GetArg(2), out var time);

		if (command.ArgCount >= 3 && command.GetArg(3).Length > 0)
			reason = command.GetArg(3);

		MuteManager muteManager = new(Database);

		playersToTarget.ForEach(player =>
		{
			if (caller!.CanTarget(player))
			{
				Mute(caller, player, time, reason, callerName, muteManager, command);
			}
		});
	}

	internal static void Mute(CCSPlayerController? caller, CCSPlayerController player, int time, string reason, string? callerName = null, MuteManager? muteManager = null, CommandInfo? command = null, bool silent = false)
	{
		if (Database == null || !player.IsValid || !player.UserId.HasValue) return;
		if (!caller.CanTarget(player)) return;

		// Set default caller name if not provided
		callerName ??= caller == null ? "Console" : caller.PlayerName;
		muteManager ??= new MuteManager(Database);

		// Get player and admin information
		var playerInfo = PlayersInfo[player.UserId.Value];
		var adminInfo = caller != null && caller.UserId.HasValue ? PlayersInfo[caller.UserId.Value] : null;

		// Set player's voice flags to muted
		player.VoiceFlags = VoiceFlags.Muted;

		// Asynchronously handle mute logic
		Task.Run(async () =>
		{
			await muteManager.MutePlayer(playerInfo, adminInfo, reason, time, 1);
		});

		// Add penalty to the player's penalty manager
		PlayerPenaltyManager.AddPenalty(player.Slot, PenaltyType.Mute, DateTime.Now.AddMinutes(time), time);

		// Determine message keys and arguments based on mute time (permanent or timed)
		var (messageKey, activityMessageKey, playerArgs, adminActivityArgs) = time == 0
			? ("sa_player_mute_message_perm", "sa_admin_mute_message_perm",
				[reason, "CALLER"],
				["CALLER", player.PlayerName, reason])
			: ("sa_player_mute_message_time", "sa_admin_mute_message_time",
				new object[] { reason, time, "CALLER" },
				new object[] { "CALLER", player.PlayerName, reason, time });

		// Display center message to the muted player
		Helper.DisplayCenterMessage(player, messageKey, callerName, playerArgs);

		// Display admin activity message to other players
		if (caller == null || !SilentPlayers.Contains(caller.Slot))
		{
			Helper.ShowAdminActivity(activityMessageKey, callerName, adminActivityArgs);
		}

		// Increment the player's total mutes count
		PlayersInfo[player.UserId.Value].TotalMutes++;

		// Log the mute command and send Discord notification
		if (!silent)
		{
			if (command == null)
				Helper.LogCommand(caller, $"css_mute {(string.IsNullOrEmpty(player.PlayerName) ? player.SteamID.ToString() : player.PlayerName)} {time} {reason}");
			else
				Helper.LogCommand(caller, command);
		}
		    
		Helper.SendDiscordPenaltyMessage(caller, player, reason, time, PenaltyType.Mute, _localizer);
	}

	[ConsoleCommand("css_addmute")]
	[RequiresPermissions("@css/chat")]
	[CommandHelper(minArgs: 1, usage: "<steamid> [time in minutes/0 perm] [reason]", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
	public void OnAddMuteCommand(CCSPlayerController? caller, CommandInfo command)
	{
		if (Database == null) return;

		// Set caller name
		var callerName = caller == null ? "Console" : caller.PlayerName;

		// Validate command arguments
		if (command.ArgCount < 2 || string.IsNullOrEmpty(command.GetArg(1))) return;

		// Validate and extract SteamID
		if (!Helper.ValidateSteamId(command.GetArg(1), out var steamId) || steamId == null)
		{
			command.ReplyToCommand("Invalid SteamID64.");
			return;
		}

		var steamid = steamId.SteamId64.ToString();
		var reason = command.ArgCount >= 3 && command.GetArg(3).Length > 0 
			? command.GetArg(3) 
			: (_localizer?["sa_unknown"] ?? "Unknown");

		MuteManager muteManager = new(Database);
		int.TryParse(command.GetArg(2), out var time);

		// Get player and admin info
		var adminInfo = caller != null && caller.UserId.HasValue ? PlayersInfo[caller.UserId.Value] : null;

		// Attempt to match player based on SteamID
		var matches = Helper.GetPlayerFromSteamid64(steamid);
		var player = matches.Count == 1 ? matches.FirstOrDefault() : null;

		if (player != null && player.IsValid)
		{
			// Check if caller can target the player
			if (!caller.CanTarget(player)) return;

			// Perform the mute for an online player
			Mute(caller, player, time, reason, callerName, muteManager, silent: true);
		}
		else
		{
			// Asynchronous mute operation for offline players
			Task.Run(async () =>
			{
				await muteManager.AddMuteBySteamid(steamid, adminInfo, reason, time, 1);
			});

			command.ReplyToCommand($"Player with steamid {steamid} is not online. Mute has been added offline.");
		}

		// Log the mute command and respond to the command
		Helper.LogCommand(caller, command);
	}

	[ConsoleCommand("css_unmute")]
	[RequiresPermissions("@css/chat")]
	[CommandHelper(minArgs: 1, usage: "<steamid or name>", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
	public void OnUnmuteCommand(CCSPlayerController? caller, CommandInfo command)
	{
		if (Database == null) return;

		var callerSteamId = caller?.SteamID.ToString() ?? "Console";
		var pattern = command.GetArg(1);
		var reason = command.GetArg(2);

		if (pattern.Length <= 1)
		{
			command.ReplyToCommand("Too short pattern to search.");
			return;
		}

		Helper.LogCommand(caller, command);
		var muteManager = new MuteManager(Database);

		// Check if pattern is a valid SteamID64
		if (Helper.ValidateSteamId(pattern, out var steamId) && steamId != null)
		{
			var matches = Helper.GetPlayerFromSteamid64(steamId.SteamId64.ToString());
			var player = matches.Count == 1 ? matches.FirstOrDefault() : null;

			if (player != null && player.IsValid)
			{
				PlayerPenaltyManager.RemovePenaltiesByType(player.Slot, PenaltyType.Mute);
				player.VoiceFlags = VoiceFlags.Normal;

				Task.Run(async () =>
				{
					await muteManager.UnmutePlayer(player.SteamID.ToString(), callerSteamId, reason, 1);
				});

				command.ReplyToCommand($"Unmuted player {player.PlayerName}.");
				return;
			}
		}

		// If not a valid SteamID64, check by player name
		var nameMatches = Helper.GetPlayerFromName(pattern);
		var namePlayer = nameMatches.Count == 1 ? nameMatches.FirstOrDefault() : null;

		if (namePlayer != null && namePlayer.IsValid)
		{
			PlayerPenaltyManager.RemovePenaltiesByType(namePlayer.Slot, PenaltyType.Mute);
			namePlayer.VoiceFlags = VoiceFlags.Normal;

			if (namePlayer.UserId.HasValue && PlayersInfo[namePlayer.UserId.Value].TotalMutes > 0)
				PlayersInfo[namePlayer.UserId.Value].TotalMutes--;

			Task.Run(async () =>
			{
				await muteManager.UnmutePlayer(namePlayer.SteamID.ToString(), callerSteamId, reason, 1);
			});

			command.ReplyToCommand($"Unmuted player {namePlayer.PlayerName}.");
		}
		else
		{
			Task.Run(async () =>
			{
				await muteManager.UnmutePlayer(pattern, callerSteamId, reason, 1);
			});

			command.ReplyToCommand($"Unmuted offline player with pattern {pattern}.");
		}
	}

	[ConsoleCommand("css_silence")]
	[RequiresPermissions("@css/chat")]
	[CommandHelper(minArgs: 1, usage: "<#userid or name> [time in minutes/0 perm] [reason]", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
	public void OnSilenceCommand(CCSPlayerController? caller, CommandInfo command)
	{
		if (Database == null) return;
		var callerName = caller == null ? "Console" : caller.PlayerName;

		var reason = _localizer?["sa_unknown"] ?? "Unknown";

		var targets = GetTarget(command);
		if (targets == null) return;
		var playersToTarget = targets.Players.Where(player => player is { IsValid: true, IsHLTV: false }).ToList();

		if (playersToTarget.Count > 1 && Config.OtherSettings.DisableDangerousCommands || playersToTarget.Count == 0)
		{
			return;
		}

		int.TryParse(command.GetArg(2), out var time);

		if (command.ArgCount >= 3 && command.GetArg(3).Length > 0)
			reason = command.GetArg(3);

		MuteManager muteManager = new(Database);

		playersToTarget.ForEach(player =>
		{
			if (caller!.CanTarget(player))
			{
				Silence(caller, player, time, reason, callerName, muteManager, command);
			}
		});
	}
		
	internal static void Silence(CCSPlayerController? caller, CCSPlayerController player, int time, string reason, string? callerName = null, MuteManager? muteManager = null, CommandInfo? command = null, bool silent = false)
	{
		if (Database == null || !player.IsValid || !player.UserId.HasValue) return;
		if (!caller.CanTarget(player)) return;

		// Set default caller name if not provided
		callerName ??= caller == null ? "Console" : caller.PlayerName;
		muteManager ??= new MuteManager(Database);

		// Get player and admin information
		var playerInfo = PlayersInfo[player.UserId.Value];
		var adminInfo = caller != null && caller.UserId.HasValue ? PlayersInfo[caller.UserId.Value] : null;

		// Asynchronously handle silence logic
		Task.Run(async () =>
		{
			await muteManager.MutePlayer(playerInfo, adminInfo, reason, time, 2); // Assuming 2 is the type for silence
		});

		// Execute tag mute if needed
		if (TagsDetected)
		{
			Server.ExecuteCommand($"css_tag_mute {player.SteamID}");
		}

		// Add penalty to the player's penalty manager
		PlayerPenaltyManager.AddPenalty(player.Slot, PenaltyType.Silence, DateTime.Now.AddMinutes(time), time);

		// Determine message keys and arguments based on silence time (permanent or timed)
		var (messageKey, activityMessageKey, playerArgs, adminActivityArgs) = time == 0
			? ("sa_player_silence_message_perm", "sa_admin_silence_message_perm",
				[reason, "CALLER"],
				["CALLER", player.PlayerName, reason])
			: ("sa_player_silence_message_time", "sa_admin_silence_message_time",
				new object[] { reason, time, "CALLER" },
				new object[] { "CALLER", player.PlayerName, reason, time });

		// Display center message to the silenced player
		Helper.DisplayCenterMessage(player, messageKey, callerName, playerArgs);

		// Display admin activity message to other players
		if (caller == null || !SilentPlayers.Contains(caller.Slot))
		{
			Helper.ShowAdminActivity(activityMessageKey, callerName, adminActivityArgs);
		}

		// Increment the player's total silences count
		PlayersInfo[player.UserId.Value].TotalSilences++;

		// Log the silence command and send Discord notification
		if (!silent)
		{
			if (command == null)
				Helper.LogCommand(caller, $"css_silence {(string.IsNullOrEmpty(player.PlayerName) ? player.SteamID.ToString() : player.PlayerName)} {time} {reason}");
			else
				Helper.LogCommand(caller, command);
		}
		    
		Helper.SendDiscordPenaltyMessage(caller, player, reason, time, PenaltyType.Silence, _localizer);
	}

	[ConsoleCommand("css_addsilence")]
	[RequiresPermissions("@css/chat")]
	[CommandHelper(minArgs: 1, usage: "<#userid or name> [time in minutes/0 perm] [reason]", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
	public void OnAddSilenceCommand(CCSPlayerController? caller, CommandInfo command)
	{
		if (Database == null) return;

		// Set caller name
		var callerName = caller == null ? "Console" : caller.PlayerName;

		// Validate command arguments
		if (command.ArgCount < 2 || string.IsNullOrEmpty(command.GetArg(1))) return;

		// Validate and extract SteamID
		if (!Helper.ValidateSteamId(command.GetArg(1), out var steamId) || steamId == null)
		{
			command.ReplyToCommand("Invalid SteamID64.");
			return;
		}

		var steamid = steamId.SteamId64.ToString();
		var reason = command.ArgCount >= 3 && command.GetArg(3).Length > 0 
			? command.GetArg(3) 
			: (_localizer?["sa_unknown"] ?? "Unknown");

		int.TryParse(command.GetArg(2), out var time);
		MuteManager muteManager = new(Database);

		// Get player and admin info
		var adminInfo = caller != null && caller.UserId.HasValue ? PlayersInfo[caller.UserId.Value] : null;

		// Attempt to match player based on SteamID
		var matches = Helper.GetPlayerFromSteamid64(steamid);
		var player = matches.Count == 1 ? matches.FirstOrDefault() : null;

		if (player != null && player.IsValid)
		{
			// Check if caller can target the player
			if (!caller.CanTarget(player)) return;

			// Perform the silence for an online player
			Silence(caller, player, time, reason, callerName, muteManager, silent: true);
		}
		else
		{
			// Asynchronous silence operation for offline players
			Task.Run(async () =>
			{
				await muteManager.AddMuteBySteamid(steamid, adminInfo, reason, time, 2);
			});

			command.ReplyToCommand($"Player with steamid {steamid} is not online. Silence has been added offline.");
		}
    
		// Log the silence command and respond to the command
		Helper.LogCommand(caller, command);
	}

	[ConsoleCommand("css_unsilence")]
	[RequiresPermissions("@css/chat")]
	[CommandHelper(minArgs: 1, usage: "<steamid or name> [reason]", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
	public void OnUnsilenceCommand(CCSPlayerController? caller, CommandInfo command)
	{
	    if (Database == null) return;

	    var callerSteamId = caller?.SteamID.ToString() ?? "Console";
	    var pattern = command.GetArg(1);
	    var reason = command.GetArg(2);

	    if (pattern.Length <= 1)
	    {
	        command.ReplyToCommand("Too short pattern to search.");
	        return;
	    }

	    Helper.LogCommand(caller, command);
	    var muteManager = new MuteManager(Database);

	    // Check if pattern is a valid SteamID64
	    if (Helper.ValidateSteamId(pattern, out var steamId) && steamId != null)
	    {
	        var matches = Helper.GetPlayerFromSteamid64(steamId.SteamId64.ToString());
	        var player = matches.Count == 1 ? matches.FirstOrDefault() : null;

	        if (player != null && player.IsValid)
	        {
	            PlayerPenaltyManager.RemovePenaltiesByType(player.Slot, PenaltyType.Silence);

	            // Reset voice flags to normal
	            player.VoiceFlags = VoiceFlags.Normal;

	            if (TagsDetected)
	                Server.ExecuteCommand($"css_tag_unmute {player.SteamID}");

	            Task.Run(async () =>
	            {
	                await muteManager.UnmutePlayer(player.SteamID.ToString(), callerSteamId, reason, 2); // Unmute by type 2 (silence)
	            });

	            command.ReplyToCommand($"Unsilenced player {player.PlayerName}.");
	            return;
	        }
	    }

	    // If not a valid SteamID64, check by player name
	    var nameMatches = Helper.GetPlayerFromName(pattern);
	    var namePlayer = nameMatches.Count == 1 ? nameMatches.FirstOrDefault() : null;

	    if (namePlayer != null && namePlayer.IsValid)
	    {
	        PlayerPenaltyManager.RemovePenaltiesByType(namePlayer.Slot, PenaltyType.Silence);

	        // Reset voice flags to normal
	        namePlayer.VoiceFlags = VoiceFlags.Normal;

	        if (namePlayer.UserId.HasValue && PlayersInfo[namePlayer.UserId.Value].TotalSilences > 0) 
	            PlayersInfo[namePlayer.UserId.Value].TotalSilences--;

	        if (TagsDetected)
	            Server.ExecuteCommand($"css_tag_unmute {namePlayer.SteamID}");

	        Task.Run(async () =>
	        {
	            await muteManager.UnmutePlayer(namePlayer.SteamID.ToString(), callerSteamId, reason, 2); // Unmute by type 2 (silence)
	        });

	        command.ReplyToCommand($"Unsilenced player {namePlayer.PlayerName}.");
	    }
	    else
	    {
	        Task.Run(async () =>
	        {
	            await muteManager.UnmutePlayer(pattern, callerSteamId, reason, 2); // Unmute by type 2 (silence)
	        });

	        command.ReplyToCommand($"Unsilenced offline player with pattern {pattern}.");
	    }
	}

}