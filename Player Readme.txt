Autopilot provides Electronic Navigation, Communication, and Targeting Systems

[url=http://forum.keenswh.com/threads/mod-autopilot.7227970/] Deutsche Übersetzung von Robinson C. [/url]

[b]There is now a different method for writing detected grids to a text panel.[/b]
Instead of writing [ <panel name> ] in an antenna's name, write [ Display Detected ] in the text panel's name.

[h1]Mod Features[/h1]
Automatic docking & landing
Patrol
Fly to another ship/station
Fly to world GPS location
Fly a certain distance relative to ship
Formations/Orientation matching
Command looping
Speed Control
Obstacle detection & collision avoidance
Engage Enemy ships/stations
Radar
Act as a missle and target a block on a an enemy ship/station
Smart Turret Control - Allows you to set priorities for your turrets.
            --This allows you to disable, but not completely destroy an enemy ship/station (grid)

[h1]Contribute[/h1]
I am looking for assistance with models, code, tutorials, and translations.
If you would like to contribute, [url=http://steamcommunity.com/workshop/filedetails/discussion/363880940/617330406650232961/] leave a message here [/url].

<discussion>
Models - Full 3D models would be ideal but even drawings would be nice.
Code - Programming experience is required; this could be a great opportunity to learn C#.
Tutorials - Think you have a good handle on how to work the mod? Prove it!
Translations - Speak another language? Good, because I don't.
</discussion>

[h1]Ingame Help[/h1]
type "/Autopilot" ingame for a list of help topics
type "/Autopilot <topic>" for information about a specific topic

[h1]Autopilot Navigation[/h1]
[url=http://steamcommunity.com/workshop/filedetails/discussion/363880940/611696927911195853/] Autopilot Navigation [/url]

<discussion>
[h1]Commands[/h1]
C <x>, <y>, <z> : for flying to specific world coordinates.
Example - [ C 0, 0, 0 : C 500, 500, 500 ] - fly to {0, 0, 0} then fly to {500, 500, 500}, will keep flying back and forth

E <range> : Any time after E is set, fly towards the nearest enemy grid. While no enemy is in range, continue following commands. Use 0 for infinite range. Use OFF to disable.
Example - [ E 0 ] - move towards any detected enemy

EXIT : stop the Autopilot, do not loop. Useful for one-way autopilot
Example - [ E 0 : C 0, 0, 0 : EXIT ] - will target any enemy that comes into range while flying to {0, 0, 0}. Upon reaching {0, 0, 0}, will stop searching for a target

G <name> : fly towards a specific friendly grid, by name of grid (Ship Name)
Example - [ G Platform : EXIT ] - Fly to a friendly grid that has "Platform" in its name, then stop

M <range> : same as E but for a missile
Example - [ M 0 ] - Attempt to crash into any enemy that can be detected.

W <seconds> : wait before travelling to the next destination
Example - [ C 0, 0 , 0 : W 60 : C 500, 500, 500 : EXIT ] - Will wait for 60 seconds after reaching {0, 0, 0}

[h1]Advanced Commands[/h1]
A <block>, <action> : Run an action on one or more blocks. <action> is case-sensitive. Autopilot will find every block that contains <block>, find the ITerminalAction that matches <action>, and apply it. Block must have faction share with remote's owner.
Example - [ A Thrust, OnOff_On ] - turn all the thrusters on

Asteroid : Disable asteroid collision avoidance, only affects the next destination.
Example - [ Asteroid : C 0,0,0 : C 1000,0,0 ] - fly to 0,0,0 ignoring asteroids, fly to 1000,0,0 avoiding asteroids

B <name> : for navigating to a specific block on a grid, will only affect the next use of G, E, or M. For friendly grids uses the display name; for hostile grids the definition name. Target block must be working.
Example - [ B Antenna : G Platform ] - fly to Antenna on Platform
Example - [ B Reactor : E 0 ] - will only target an enemy with a working reactor
B <name>, <direction> : <direction> indicates which direction to approach block from when landing
Example - [ L Landing Gear : B Beacon, Rightward : G Platform : W 60 ] - Attach landing gear to the right side of beacon on Platform

F <r>, <u>, <b> : fly a distance relative to self. coordinates are rightward, upward, backwards
Example - [ F 0, 0, -1000 ] - fly 1000m forward
F <distance> <direction>, ... : generic form is a comma separated list of distances and directions
Example - [ F 1000 forward ] - fly 1000m forward
Example - [ F 1000 forward, 200 downward ] - fly to a point 1000m ahead and 200m below remote control

L : landing block. the block on the same grid as the Remote that will be used to land. To land there must be a landing block, a target block, and a target grid [ L <localBlock> : B <targetBlock> : G <targetGrid> ]. If there is a wait command before the next destination, the grid will wait while attached. If there is a LOCK command before the next destination, the grid will not separate. If there is an EXIT command before the next destination, the grid will stay attached.
Example - [ L Connector : B Connector : G Platform : W60 : C 0,0,0 ] - attach local connector to connector on Platform, wait 60 seconds, detach, fly to {0,0,0}

LOCK : leave landing block in locked state ( do not disconnect )
Example - [ L Connector : B Connector : G Pickup : LOCK : F 0, 0, 0 ] - connect with Pickup, fly to {0, 0, 0}

O <r>, <u>, <b> : destination offset, only works if there is a block target, cleared after reaching destination. coordinates are right, up, back. NO_PATH if offset destination is inside the boundaries of any grid (including destination).
Example - [ O 0, 500, 0 : B Antenna : G Platform ] - fly to 500m above Antenna on Platform
O <distance> <direction>, ... : generic form is a comma separated list of distances and directions
Example - [ O 500 upward : B Antenna : G Platform ] - fly to 500m above Antenna on Platform
Example - [ O 100 forward, 500 upward : B Antenna : G Platform ] - fly to 100m ahead of and 500m above Antenna

P <range> : how close the grid needs to fly to the destination, default is 100m. Ignored for M.
Example - [ P 10 : F 0, 0, -100 ] - Fly 100m forward

R <f> : match direction, needs a target block. <f> is the target block's direction that the Remote Control will face
Example - [ R Forward : B Antenna : G Platform ] - fly to Antenna on Platform, then face Remote Control to antenna forward
R <f>, <u> : match orientation (direction and roll). <f> as above. <u> which block direction will be Remote Control's Up
Example - [ R Forward, Upward : B Antenna : G Platform ] - fly to Antenna on Platform, then face Remote Control to antenna forward, then roll so that Antenna's Upward will match Remote Control's upward.

T <name> : fetch commands from the public text of a text panel named <name>, starting at the first [ and ending at the first following ]
Example - [ T Command Panel ] - fetch commands from "Command Panel"
T <name>, <sub> : as above, the search for [ and ] will begin after the first occurrence of <sub>. It is recommend to make <sub> unique, so that it will not be confused with anything else.
Example - [ T Command Panel, {Line 4} ] - where "Command Panel" contains ... {Line 4} [ C 0,0,0 ] ... fly to {0,0,0}

V <cruise> : when travelling faster than <cruise> reduce thrust (zero or very little thrust)
Example - [ V 10 : C 0, 0, 0 : C 500, 500, 500 ] - fly back and forth between {0, 0, 0} and {500, 500, 500}, cruising when above 10m/s. The default for <cruise> is set in the settings file.
V <cruise>, <slow> : when speed is below <cruise>, accelerate; when speed is between <cruise> and <slow>, cruise; when speed is above <slow>, decelerate. The default for <cruise> is set in the settings file, the default for <slow> is infinity (practically).
Example - [ V 10, 20 : C 0, 0, 0 : C 500, 500, 500 ] - fly back and forth between {0, 0, 0} and {500, 500, 500}, staying between 10m/s and 20m/s.

[b]Directions[/b] : can be { Forward, Backward, Leftward, Rightward, Upward, Downward }. Autopilot only checks the first letter, so abbreviations will work. For example, "Forward" can be "Fore" or "F"
[b]Distances[/b] : for F, O, and P can be modified by km(kilometres) or Mm(megametres). For example, "3.5M" or "3.5Mm" is 3.5 megametres or 3 500 kilometres.

[h1]More Examples[/h1]
[B Thrust : M 0] Attempt to crash into the nearest enemy grid's thrusters.
[E 300 : C 0,0,0 : C 1000,0,0] Patrol between two points until an enemy grid comes into range (300m), then fly towards enemy grid.
[P 10 : F 0,0,-100 : M 0] Fly forward 100 metres then convert to missile. If no enemy is found, keep moving forward 100m at a time. Can lock onto an enemy any time after first move.
[P 1000 : G MiningBase : W 600 : G MainBase : W 300] fly to within 1km of MiningBase, wait for 10 minutes, fly to within 1km of MainBase, wait 5 minutes.
[ R Forward, Upward : B PlatConn : L MyConn : G Platform ] set the orientation of the remote to Forward, Upward (relative to PlatConn) and dock. This is useful when a specific docking orientation is required due to lack of space.

[h1]Autopilot States[/h1]
<OFF> remote control has not searched for commands, EXIT was reached, or the remote control is not ready
<PATHFINDING> searching for a path towards the destination
<NO_PATH> could not find a path to the destination
<NO_DEST> could not find a valid target or destination, this state is usually temporary
<ERROR:(index)> Displays the index of the commands that could not be executed. The first command is at 0.
<WAITING:(time)> a wait command was reached, display time remaining
<ROTATING> rotating the ship
<MOVING> heading towards the next stop
<STOPPING> stopping the grid
<MISSILE> found a target, going to hit it
<ENGAGING> found a target, flying towards it
<LANDED> grid is landed
<PLAYER> A player is controlling grid
<GET_OUT_OF_SEAT> Autopilot cannot disconnect a connector or landing gear while a player is in a seat.

[h1]More Information[/h1]
To reset the Autopilot: disable "Control Thrusters", wait a second, turn it back on.
If you reset the Autopilot while landed, it will not separate before moving.

In order for Autopilot to control a grid, it must have a gyroscope, have thrusters in every direction, must not be currently controlled, and must have an owner (NPC is OK).

All commands are in the display name of a Remote Control block.
[] All commands are contained within a single set of square brackets
<> Do not use angle brackets in your Remote Control's name
:; Commands are separated by colons and/or semicolons
Variables P and V affect all destinations that come after
  Interpreter ignores all spaces
Aa Interpreter is case insensitve

All distances and coordinates are in metres, speed is in metres per second
If there are multiple Remote Controls with commands, one will be picked arbitrarily.
The direction that the Remote Control is facing is the direction the ship will fly in.
When the end of the commands is reached, Autopilot will start back at the first command.
</discussion>

[h1]Antenna Relay and Radar[/h1]
Each radio antenna, beacon, and radar transmits its location to radio antennae that are inside its broadcast range.
Each radio antenna relays the information that it has to friendly radio antennae inside its broadcast range.
Each laser antenna relays the information that is has to the laser antenna it is connected to.
Antennae, radars, and remote controls in attached grids share information.

Radar can detect any grid; the grid does not have to be broadcasting or even have power. The distance a grid can be detected by radar is based on the size of the grid and the broadcast range (power) of the radar. The maximum distance a grid can be detected is 50% of the radar's power and the minimum distance is 5% of the radar's power.

Each antenna and remote control keeps track of the last time a grid was seen, where it was, and its velocity. This information is used to predict the current location of a grid.

It is not possible for Autopilot to display entities on the HUD.

[h1]Block Communication[/h1]
[url=http://steamcommunity.com/sharedfiles/filedetails/?id=391453613] This script [/url] can be used to send and receive messages, filter detected grids, and execute actions based on detected grids.
Messages can be sent from one programmable block to another. Block communication will use antenna relay to send messages to other grids.
Block Communication can read detected grid information, apply filters, execute actions, and write to a TextPanel.
For usage, see the script itself.

[h1]Smart Turret Control[/h1]
Turrets can be given specific instructions on which targets to shoot; for blocks the turret will only target blocks that are working.
In order for Smart Turret Control to function, a turret must have square brackets in its name.

Block targets are fetched from the turret's name [ <definition1>, <definition2>, ... ] and target working hostile blocks in order.
Example - [ Turret, Rocket, Gatling, Reactor, Battery ] - First shoot all turrets, then rocket launchers, then gatling weapons, then reactors, then batteries.

[b]Priorities[/b] - highest to lowest
If Target Missiles is enabled, shoot missiles that are approaching the turret.
If Target Meteors is enabled, shoot meteors that are approaching the turret.
If Target Characters is enabled, shoot enemy characters.
If a list of block targets is provided, shoot working blocks.
If Target Moving Objects is enabled, shoot hostile grids that are approaching the turret.

[h1]Settings[/h1]
The file at "%AppData%\SpaceEngineers\Storage\363880940.sbm_Autopilot\AutopilotSettings.txt" contains the settings for Autopilot.
To reset a value to its default, simply delete it.
bAllowAutopilot - this mod can control the movement of grids
bAllowRadar - radar can be used to detect grids, otherwise functions as a beacon
bAllowTurretControl - enables Smart Turret Control
fDefaultSpeed - the desired minimum speed, when not using V
fMaxSpeed - the maximum speed Autopilot is allowed to fly at

[h1]Known Issues[/h1]
Autopilot cannot always control a grid if there are grids attached with landing gear. In this case, the remote control will display <NO_PATH>.

Sometimes when pasting a grid in creative mode, Autopilot will not run properly for the pasted grid. I am still working to resolve this issue.

There is a bug in Space Engineers that occurs when unlocking a connector or landing gear while a player is in a cockpit or passenger seat. Autopilot will not take control if this bug occurs.
see http://forums.keenswh.com/post/1-061-rare-remote-control-lost-control-bug-7215218
Autopilot will not unlock a connector or landing gear while any player is in a cockpit or passenger seat. The remote control will display <GET_OUT_OF_SEAT> until all cockpits and passenger seats are empty, then it will unlock the connector or landing gear.

Merge blocks may conflict with Autopilot. Autopilot cannot detect a merge, so you will have to sort it out yourself.

[b]Public Domain License[/b]
To the extent possible under law, Alexander Durand has waived all copyright and related or neighbouring rights to Autopilot. This work is published from: Canada.
http://creativecommons.org/publicdomain/zero/1.0/

[h1]Credits[/h1]
GitMaster Extraordinaire - [uRxP]DrChocolate
Multiplayer Testing - Degalus

[b]Links[/b]
[url=http://www.nexusmods.com/spaceengineers/mods/24/?] On Nexus Mods [/url]
[url=http://steamcommunity.com/sharedfiles/filedetails/?id=363880940] On Steam [/url]
[url=https://github.com/Rynchodon/Autopilot] On GitHub [/url]

[b]I will not be responding to comments posted below, use one of these links or start a new discussion.[/b]
[url=http://steamcommunity.com/workshop/filedetails/discussion/363880940/611696927911256823/] Request a Feature [/url]
[url=http://steamcommunity.com/workshop/filedetails/discussion/363880940/622954023412161440/] Report a Bug [/url]
[url=http://steamcommunity.com/workshop/filedetails/discussion/363880940/611696927925580310/] Ask a Question [/url]
