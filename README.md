# Autopilot
Autopilot is a mod for [Space Engineers](http://www.spaceengineersgame.com/)
that provides a simple interface for automated sequences of piloting actions
like navigation, engagement, collision avoidance, docking, and more.

It also includes radar functionality for picking up non-broadcasting objects
depending on their radar signature and distance. Radar information is
distributed across antenna networks.

Please see the [steam page]
(http://steamcommunity.com/sharedfiles/filedetails/?id=363880940) for a full
feature list.

## Getting started
If you'd simply like to use the mod, please subscribe to it via the Steam
Workshop as you normally would and enable it on your save or server.

To download a development-ready local copy of this mod instead, follow the steps
below.

### Requirements

To work with Autopilot, ensure you have:

* [Git](http://git-scm.com/)
* [Python](https://www.python.org/)
* A working understanding of [C#]
(https://msdn.microsoft.com/en-us/library/67ef8sbd.aspx)

### Installation

First, [create your own github fork]
(https://help.github.com/articles/fork-a-repo/) to hold your changes.

Next, clone to repo to your machine:

```
git clone https://github.com/username/autopilot
```

Finally, make sure you can run the build task that packages your current
code state and deploys it to Space Engineers:

```
python build.py
```

After that, you should be able to load Autopilot and Autopilot Dev (has
logging) directly from the Space Engineers mod screen.

**Ensure you rerun the build task each time you're ready to test your changes.**

## Roadmap

Please see the [steam page]
(http://steamcommunity.com/sharedfiles/filedetails/?id=363880940) for the
current Roadmap.

## Contributing

Please submit bug reports and feature requests on the [steam page]
(http://steamcommunity.com/sharedfiles/filedetails/?id=363880940) and its related
discussions.

## License
CC0 1.0 Universal (CC0 1.0), see LICENSE.
