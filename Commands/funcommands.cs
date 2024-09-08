using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;

namespace CS2_SimpleAdmin;

public partial class CS2_SimpleAdmin
{
	[ConsoleCommand("css_noclip", "Noclip a player.")]
	[CommandHelper(1, "<#userid or name>")]
	[RequiresPermissions("@css/cheats")]
	public void OnNoclipCommand(CCSPlayerController? caller, CommandInfo command)
	{
		var callerName = caller == null ? "Console" : caller.PlayerName;

		var targets = GetTarget(command);
		if (targets == null) return;
		var playersToTarget = targets.Players.Where(player =>
			player.IsValid &&
			player is { PawnIsAlive: true, IsHLTV: false, Connected: PlayerConnectedState.PlayerConnected }).ToList();
			
		playersToTarget.ForEach(player =>
		{
			if (caller!.CanTarget(player))
			{
                NoClip(caller, player, callerName);
			}
		});
	}

	internal static void NoClip(CCSPlayerController? caller, CCSPlayerController player, string? callerName = null, CommandInfo? command = null)
	{
		if (!player.IsValid) return;
		if (!caller.CanTarget(player)) return;

		// Set default caller name if not provided
		callerName ??= caller != null ? caller.PlayerName : "Console";

		// Toggle no-clip mode for the player
		player.Pawn.Value?.ToggleNoclip();

		// Determine message keys and arguments for the no-clip notification
		var (activityMessageKey, adminActivityArgs) =
			("sa_admin_noclip_message",
				new object[] { callerName, player.PlayerName });

		// Display admin activity message to other players
		if (caller == null || !SilentPlayers.Contains(caller.Slot))
		{
			Helper.ShowAdminActivity(activityMessageKey, callerName, adminActivityArgs);
		}

		// Log the command
		if (command == null)
		{
			Helper.LogCommand(caller, $"css_noclip {(string.IsNullOrEmpty(player.PlayerName) ? player.SteamID.ToString() : player.PlayerName)}");
		}
		else
		{
			Helper.LogCommand(caller, command);
		}
	}

	[ConsoleCommand("css_freeze", "Freeze a player.")]
	[CommandHelper(1, "<#userid or name> [duration]")]
	[RequiresPermissions("@css/slay")]
	public void OnFreezeCommand(CCSPlayerController? caller, CommandInfo command)
	{
		var callerName = caller == null ? "Console" : caller.PlayerName;
		int.TryParse(command.GetArg(2), out var time);

		var targets = GetTarget(command);
		if (targets == null) return;
		var playersToTarget = targets.Players.Where(player => player is { IsValid: true, PawnIsAlive: true, IsHLTV: false }).ToList();
			
		playersToTarget.ForEach(player =>
		{
			if (caller!.CanTarget(player))
			{
				Freeze(caller, player, time, callerName, command);
			}
		});
	}

	internal static void Freeze(CCSPlayerController? caller, CCSPlayerController player, int time, string? callerName = null, CommandInfo? command = null)
	{
		if (!player.IsValid) return;
		if (!caller.CanTarget(player)) return;

		// Set default caller name if not provided
		callerName ??= caller != null ? caller.PlayerName : "Console";

		// Freeze player pawn
		player.Pawn.Value?.Freeze();

		// Determine message keys and arguments for the freeze notification
		var (activityMessageKey, adminActivityArgs) = 
			("sa_admin_freeze_message", 
				new object[] { callerName, player.PlayerName });

		// Display admin activity message to other players
		if (caller == null || !SilentPlayers.Contains(caller.Slot))
		{
			Helper.ShowAdminActivity(activityMessageKey, callerName, adminActivityArgs);
		}

		// Schedule unfreeze for the player if time is specified
		if (time > 0)
		{
			Instance.AddTimer(time, () => player.Pawn.Value?.Unfreeze(), CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
		}

		// Log the command and send Discord notification
		if (command == null)
			Helper.LogCommand(caller, $"css_freeze {(string.IsNullOrEmpty(player.PlayerName) ? player.SteamID.ToString() : player.PlayerName)} {time}");
		else
			Helper.LogCommand(caller, command);
	}

	[ConsoleCommand("css_unfreeze", "Unfreeze a player.")]
	[CommandHelper(1, "<#userid or name>")]
	[RequiresPermissions("@css/slay")]
	public void OnUnfreezeCommand(CCSPlayerController? caller, CommandInfo command)
	{
		var callerName = caller == null ? "Console" : caller.PlayerName;

		var targets = GetTarget(command);
		if (targets == null) return;
		var playersToTarget = targets.Players.Where(player => player is { IsValid: true, PawnIsAlive: true, IsHLTV: false }).ToList();

		playersToTarget.ForEach(player =>
		{
			Unfreeze(caller, player, callerName, command);
		});
	}

	public void Unfreeze(CCSPlayerController? caller, CCSPlayerController player, string? callerName = null, CommandInfo? command = null)
	{
		if (!player.IsValid) return;
		if (!caller.CanTarget(player)) return;

		// Set default caller name if not provided
		callerName ??= caller != null ? caller.PlayerName : "Console";

		// Unfreeze player pawn
		player.Pawn.Value?.Unfreeze();

		// Determine message keys and arguments for the unfreeze notification
		var (activityMessageKey, adminActivityArgs) = 
			("sa_admin_unfreeze_message", 
				new object[] { callerName, player.PlayerName });

		// Display admin activity message to other players
		if (caller == null || !SilentPlayers.Contains(caller.Slot))
		{
			Helper.ShowAdminActivity(activityMessageKey, callerName, adminActivityArgs);
		}

		// Log the command and send Discord notification
		if (command == null)
			Helper.LogCommand(caller, $"css_unfreeze {(string.IsNullOrEmpty(player.PlayerName) ? player.SteamID.ToString() : player.PlayerName)}");
		else
			Helper.LogCommand(caller, command);
	}
}