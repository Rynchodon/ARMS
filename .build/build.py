# build.py
#
# This script combines the individual module folders into a single structure
# for Space Engineers to load (and a bunch of other useful deploy tasks)
#
# It will create three mods,
#   "%AppData%\SpaceEngineers\Mods\ARMS",
#   "%AppData%\SpaceEngineers\Mods\ARMS Dev", and
#   "%AppData%\SpaceEngineers\Mods\ARMS Model".
#
# ARMS is the release version
# ARMS Dev only has scripts and has logging enabled
# ARMS Model has data files, models, and textures

import datetime, errno, logging, os.path, re, shutil, stat, subprocess, sys, xml.etree.ElementTree as ET

logging.basicConfig(filename = "build.log", filemode = 'w', format = '%(asctime)s %(levelname)s: %(message)s', level = logging.DEBUG)

# script directories
scriptDir = os.path.dirname(os.path.realpath(sys.argv[0]))
buildIni = scriptDir + "\\build.ini"
buildIniTemplate = scriptDir + "\\build-template.ini"
startDir = os.path.split(scriptDir)[0]
cSharp = startDir + "/Scripts/"
modules = []
ignoreDirs = [ "bin", "obj", "Properties" ] # these are case-sensitive

# paths files are moved to
finalDir = os.getenv('APPDATA') + '\SpaceEngineers\Mods\ARMS'
finalDirDev = finalDir + ' Dev'
finalDirModel = finalDir + ' Model'

# in case build.ini is missing variables
SpaceEngineers = os.devnull
Zip7 = os.devnull


def createDir(l_dir):
	if not os.path.exists(l_dir):
		#logging.info ("making: "+l_dir)
		os.makedirs(l_dir)


def eraseDir(l_dir):
	if os.path.isdir(l_dir):
		logging.info ("deleting: "+l_dir)
		shutil.rmtree(l_dir)


# method that takes a module name and moves the files
def archiveScripts(l_source):
	logging.info("Archiving scripts from " + l_source)
	l_sourceDir = cSharp + l_source
	l_archiveDir = startDir + "\Archive\\" + l_source

	for path, dirs, files in os.walk(l_sourceDir):
		for ignore in ignoreDirs:
			if (ignore in dirs):
				dirs.remove(ignore)

		os.chdir(path)

		for file in files:
			if not file.lower().endswith(".cs"):
				continue

			# for archive, add date and time to file name
			createDir(l_archiveDir)
			d = datetime.datetime.fromtimestamp(os.path.getmtime(file))
			formated = str(d.year)+"-"+str(d.month).zfill(2)+"-"+str(d.day).zfill(2)+"_"+str(d.hour).zfill(2)+"-"+str(d.minute).zfill(2)+"_"+file
			archive = l_archiveDir +"\\"+formated
			try:
				os.chmod(archive, stat.S_IWRITE)
			except OSError:
				pass
			shutil.copyfile(file, archive)
			os.chmod(archive, stat.S_IREAD)


def copyWithExtension(l_from, l_to, l_ext, log):
	# delete orphan files
	for path, dirs, files, in os.walk(l_to):
		for file in files:
			source = path.replace(l_to, l_from) + '/' + file
			if not os.path.isfile(source):
				logging.info ("\tdeleting orphan file: " + file)
				os.remove(path + '/' + file)
	
	l_ext = l_ext.lower()
	for path, dirs, files, in os.walk(l_from):
		for file in files:
			if file.lower().endswith(l_ext):
				target = path.replace(l_from, l_to)
				sourceFile = path + '/' + file
				if os.path.isdir(target):
					targetFile = target + '/' + file
					if (os.path.exists(targetFile) and os.path.getmtime(targetFile) == os.path.getmtime(sourceFile)):
						continue
				else:
					createDir(target)
				if log:
					logging.info ("Copying file: " + file)
				shutil.copy2(sourceFile, target)
				

if not os.path.exists(buildIni):
	shutil.copy(buildIniTemplate, buildIni)

exec(open(buildIni).read())

if (not os.path.exists(SpaceEngineers)):
	logging.info ("You must set the path to SpaceEngineers in build.ini")
	sys.exit(11)

if (len(sys.argv) < 2):
	logging.error ("ERROR: Build configuration not specified")
	sys.exit(12)


logging.info("Build is " + str(sys.argv[1]))
source = cSharp + 'bin/x64/' + sys.argv[1] + '/ARMS.dll'
if (not os.path.exists(source)):
	logging.error("Build not found")
	sys.exit(13)
target = SpaceEngineers + '/Bin64'
if (not os.path.exists(target)):
	logging.error("Not path to Space Engineers: " + SpaceEngineers)
	sys.exit(14)
shutil.copy2(source, target)
logging.info("Copied dll to " + target)
target = SpaceEngineers + '/DedicatedServer64'
if (not os.path.exists(target)):
	logging.error("Not path to Space Engineers: " + SpaceEngineers)
	sys.exit(15)
shutil.copy2(source, target)
logging.info("Copied dll to " + target)

createDir(finalDir)
createDir(finalDirDev)

# erase old data
eraseDir(finalDir + '\\Data')
eraseDir(finalDirDev + '\\Data')
eraseDir(finalDirModel + '\\Data')

# get modules
os.chdir(startDir + '/Scripts/')
for file in os.listdir(startDir + '/Scripts/'):
	if file[0] == '.' or file == "Programmable":
		continue
	if (file in ignoreDirs):
		continue
	if os.path.isdir(file):
		modules.append(file)

# copy data, models, and textures
copyWithExtension(startDir + '/Audio/', finalDir + '/Audio/', '.xwm', True)
copyWithExtension(startDir + '/Data/', finalDir + '/Data/', '.sbc', True)
copyWithExtension(startDir + '/Models/', finalDir + '/Models/', '.mwm', True)
copyWithExtension(startDir + '/Textures/', finalDir + '/Textures/', '.dds', True)
copyWithExtension(startDir + '/Audio/', finalDirModel + '/Audio/', '.xwm', False)
copyWithExtension(startDir + '/Data/', finalDirModel + '/Data/', '.sbc', False)
copyWithExtension(startDir + '/Models/', finalDirModel + '/Models/', '.mwm', False)
copyWithExtension(startDir + '/Textures/', finalDirModel + '/Textures/', '.dds', False)
eraseDir(finalDirDev + '/Models/')
eraseDir(finalDirDev + '/Textures/')

# build scripts
for module in modules[:]:
	archiveScripts(module)

#    Pack Archive

os.chdir(startDir)

if not os.path.exists(Zip7):
	logging.info('\nNot running 7-Zip')
	sys.exit()

size = 0
for path, dirs, files in os.walk('Archive'):
	for f in files:
		fp = os.path.join(path, f)
		size += os.path.getsize(fp)
if (size < 10000000):
	sys.exit()

logging.info("\n7-Zip running")

cmd = [Zip7, 'u', 'Archive.7z', 'Archive']
process = subprocess.Popen(cmd, stdout = open(os.devnull, 'wb'))
process.wait()

if process.returncode != 0:
	logging.error("\n7-Zip failed\n")
	sys.exit(process.returncode)

logging.info("7-Zip finished\n")

# copied from http://stackoverflow.com/questions/1213706/what-user-do-python-scripts-run-as-in-windows/1214935#1214935
def handleRemoveReadonly(func, path, exc):
	excvalue = exc[1]
	if func in (os.rmdir, os.remove) and excvalue.errno == errno.EACCES:
		os.chmod(path, stat.S_IRWXU| stat.S_IRWXG| stat.S_IRWXO) # 0777
		func(path)
	else:
		raise

shutil.rmtree('Archive', ignore_errors=False, onerror=handleRemoveReadonly)
