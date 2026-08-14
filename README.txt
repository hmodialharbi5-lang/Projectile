Boss Projectile Multiplier - TShock 6.1.0

IMPORTANT:
This ZIP is a build-ready source project, not a precompiled DLL. Put the matching
TShock 6.1.0 assemblies into the lib folder, then build with .NET 9.

Commands:
  /bpm on
  /bpm off
  /bpm status
  /bpm count
  /bpm set <player> <number>
  /bpm reset <player>
  /bpm reload

Permission:
  bpm.admin

Progress starts at 1x and increases by 1 when a player is credited with a boss kill.

NOTE:
The exact projectile duplication hook is build-sensitive in Terraria 1.4.5.x. This
starter project includes the progression/commands and intentionally avoids the old
ItemCheck(int) hook. The projectile duplication portion must be wired against the
actual TShock 6.1.0/TerrariaServer assemblies before deployment.


PROJECTILE BEHAVIOR REQUEST
The intended effect is NOT a spread pattern. Extra projectiles should travel
parallel to the original shot, adding another projectile in front/alongside the
existing line as the multiplier increases:

1 kill = 2 total
2 kills = 3 total
3 kills = 4 total
...
and it continues upward after 5, 10, etc.

The reference image in this ZIP illustrates the intended visual behavior.
