# attempts to locate Space Engineers and git, it will try common paths if build.ini is incorrect

if (not os.path.exists(GitExe)):
	GitExe = r"C:\Program Files (x86)\Git\bin\git.exe"
	if (os.path.exists(GitExe)):
		logging.info("Git in Program Files (x86)")
	else:
		GitExe = r"C:\Program Files\Git\bin\git.exe"
		if (os.path.exists(GitExe)):
			logging.info("Git in Program Files")
		else:
			GitHubPath = os.getenv('LOCALAPPDATA') + "\\GitHub\\"
			if (os.path.exists(GitHubPath)):
				for f in os.listdir(GitHubPath):
					if (f.startswith('PortableGit_')):
						logging.info("Git in " + str(f))
						GitExe = GitHubPath + str(f) + "\\cmd\\git.exe"
						break

try:
	if (not os.path.exists(SpaceEngineers)):
		paths = [r"C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers",
						 r"C:\Program Files\Steam\steamapps\common\SpaceEngineers",
						 r"C:\Games\Steam\steamapps\common\SpaceEngineers"]
		for SpaceEngineers in paths:
			if (os.path.exists(SpaceEngineers)):
				logging.info("Space Engineers located at " + SpaceEngineers)
				break
except NameError:
	# SpaceEngineers variable was not defined, no need to search for Space Engineers
	pass

if os.path.exists(GitExe):
	proc = subprocess.Popen([GitExe, "describe", "--always", "--dirty", "--tags"], stdout=subprocess.PIPE)
	gitCommit = str(proc.stdout.read())
	gitCommit = gitCommit[2:len(gitCommit)-3]
else:
	path = os.path.dirname(os.path.realpath(sys.argv[0]))
	for c in range(0, 100):
		pathToDotGit = path + r"\.git"
		if (os.path.exists(pathToDotGit)):
			logging.info("Git folder located at " + pathToDotGit)
			break
		upOne = os.path.dirname(path)
		if (path == upOne):
			logging.error("Hit root directory without finding .git folder")
			sys.exit()
		path = upOne
	
	pathToDotGit = pathToDotGit + "\\"
	path = pathToDotGit + 'HEAD'
	file = open(path, 'r')
	text = file.read()
	file.close()
	if (text.startswith('ref: ')):
		path = pathToDotGit + text[5:len(text) - 1]
		if (os.path.exists(path)):
			file = open (path, 'r')
			gitCommit = file.read()[:7]
			file.close()
		else:
			logging.error("Does not exist: " + path)
	else:
		gitCommit = text[:7]
logging.info("Commit: " + gitCommit)