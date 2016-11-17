pathToDotGit = os.path.dirname(os.path.realpath(sys.argv[0])) + r"\..\.git\\"

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

if os.path.exists(GitExe):
	proc = subprocess.Popen([GitExe, "describe", "--always", "--dirty", "--tags"], stdout=subprocess.PIPE)
	gitCommit = str(proc.stdout.read())
	gitCommit = gitCommit[2:len(gitCommit)-3]
else:
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