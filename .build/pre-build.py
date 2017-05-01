# pre-build task for Visual Studio
# updates AssemblyVersion revision number 

import logging, os.path, re, shutil, subprocess, sys

scriptDir = os.path.dirname(os.path.realpath(sys.argv[0]))

logging.basicConfig(filename = scriptDir + r"\pre-build.log", filemode = 'w', format = '%(asctime)s %(levelname)s: %(message)s', level = logging.DEBUG)

buildIni = scriptDir + r"\build.ini"
buildIniTemplate = scriptDir + r"\build-template.ini"
pathToVersionInfo = scriptDir + r"\..\Scripts\Properties\VersionInfo.cs"
pathToVersionInfoUser = scriptDir + r"\..\Scripts\Properties\VersionInfo - User.cs"
GitExe = os.devnull

if not os.path.exists(buildIni):
	logging.info("creating build.ini")
	shutil.copy(buildIniTemplate, buildIni)

exec(open(buildIni).read())
exec(open(scriptDir + r"\find-git.py").read())

if (not os.path.exists(GitExe)):
	logging.warning("Could not locate git, cannot update revision")
	sys.exit(0)

if len(sys.argv) > 1 and 'release' in sys.argv[1].lower():
	logging.info('build: ' + sys.argv[1])
	revision = 0
else:
	match = re.match('.*-(\d+)-g', gitCommit)
	if match != None:
		revision = int(match.group(1))
	else:
		revision = 0
	
	match = re.match('.*-dirty', gitCommit)
	if match != None:
		revision = revision + 1
	
logging.info("revision: " + str(revision))

allLines = []
pattern = re.compile('(\[assembly: AssemblyVersion\("\d*\.\d*\.\d*\.)(\d*)("\)\])')
for line in open(pathToVersionInfo):
	match = pattern.match(line)
	if (match != None):
		allLines.append(match.group(1) + str(revision) + match.group(3))
	else:
		allLines.append(line)

output = open(pathToVersionInfoUser, 'w')
output.writelines(allLines)
output.write('\n')
output.close()
