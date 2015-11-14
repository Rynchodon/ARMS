# build.py
#
# This script combines the individual module folders into a single structure
# for Space Engineers to load (and a bunch of other useful deploy tasks)
#
# It will create two mods,
#   "%AppData%\SpaceEngineers\Mods\ARMS" and
#   "%AppData%\SpaceEngineers\Mods\ARMS Dev".
#
# The Dev version has logging enabled

import datetime, errno, os.path, re, shutil, stat, subprocess, sys, xml.etree.ElementTree as ET

# script directories
scriptDir = os.path.dirname(os.path.realpath(sys.argv[0]))
buildIni = scriptDir + "\\build.ini"
buildIniTemplate = scriptDir + "\\build-template.ini"
startDir = os.path.split(scriptDir)[0]
cSharp = startDir + "/Scripts/"

# paths files are moved to
finalDir = os.getenv('APPDATA') + '\SpaceEngineers\Mods\ARMS'
finalDirDev = finalDir + ' Dev'

# do not change or else log file and settings file will be moved
finalScript = finalDir + '\Data\Scripts\Autopilot\\'
finalScriptDev = finalDirDev + '\Data\Scripts\Autopilot\\'

# in case build.ini is missing variables
Zip7 = os.devnull

modules = []


def createDir(l_dir):
	if not os.path.exists(l_dir):
		#print ("making: "+l_dir)
		os.makedirs(l_dir)


def eraseDir(l_dir):
	if os.path.exists(l_dir):
		#print ("deleting: "+l_dir)
		shutil.rmtree(l_dir)


# method that takes a module name and moves the files
def copyScripts(l_source):
	print("copying scripts from "+l_source)
	l_sourceDir = cSharp + l_source
	l_dataDir = startDir + "\\" + l_source + "\Data"
	l_archiveDir = startDir + "\Archive\\" + l_source

	ignoreDirs = [ "bin", "obj", "Properties" ] # these are case-sensitive

	for path, dirs, files in os.walk(l_sourceDir):
		for ignore in ignoreDirs:
			if (ignore in dirs):
				dirs.remove(ignore)

		os.chdir(path)

		nsPath = path.replace(l_sourceDir,'')
		nsStr = nsPath.replace("\\","") + '.' if nsPath != '' else ''

		for file in files:
			if not file.lower().endswith(".cs"):
				continue

			#print ("file is "+file)
			lines = open(file, 'r').readlines()

			if (len(lines) == 0 or  "skip file on build" in lines[0]):
				#print ("skipping "+file)
				continue

			l_destFileName =  l_source + '.' + nsStr + file
			l_destFile = finalScript + l_destFileName
			l_destFileDev = finalScriptDev + l_destFileName

			# fake the pre-processor
			# remove symbols so scripts will compile
			# remove Conditional in Dev version
			# compiler will still remove Conditional statements in released version
			destFile = open(l_destFile, 'w')
			destFileDev = open(l_destFileDev, 'w')
			for line in lines:
				if (not line.lstrip().startswith("//")):
					if ("#define LOG_ENABLED" in line): # could not make startswith work
						destFile.write("// pre-processor symbol removed by build.py\n")
						destFileDev.write("// pre-processor symbol removed by build.py\n")
						continue
					if ("System.Diagnostics.Conditional" in line):
						destFile.write(line)
						destFileDev.write("// Conditional removed by build.py\n")
						continue
				destFile.write(line)
				destFileDev.write(line)

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


def build_help():
	replaceIn_file=r"Help.cs"
	replaceIn_DirSource= startDir + r"\Scripts\Autopilot"

	readme_file= startDir + r"\Steam Description\Autopilot Navigation.txt"

	replaceIn_write_name = finalScript+"\\Autopilot."+replaceIn_file
	replaceIn_writeDev_name = finalScriptDev+"\\Autopilot."+replaceIn_file

	replaceIn_read = open(replaceIn_DirSource+"\\"+replaceIn_file, 'r')
	replaceIn_write = open(replaceIn_write_name+".tmp", 'w')
	replaceIn_writeDev = open(replaceIn_writeDev_name+".tmp", 'w')
	readme_read = open(readme_file, 'r')

	#    get all commands
	readme_lines = readme_read.readlines()
	command_lines = []
	command_current = ""
	index = 0
	position = 0
	while (position == 0):
		if ("[h1]Commands[/h1]" in readme_lines[index]):
			position+=1
		else:
			index+=1
	while (0==0):
		index+=1
		line_cur = readme_lines[index]

		if ("Advanced Commands" in line_cur):
			continue
		if ("\n" is line_cur):
			continue
		if ("[h1]" in line_cur):
			if ("[/h1]" in line_cur):
				if (command_current):
					command_lines.append(command_current) # append current
				break

		line_cur = line_cur.replace("\"", "\"\"")

		if ("Example - " in line_cur):
			command_current += line_cur # attach to current
		else:
			if (command_current):
				command_lines.append(command_current) # append current
			command_current = line_cur # start new command

	#    copy all commands
	for line in replaceIn_read.readlines():
		if ("fill commands by build" in line):
			# write from commands
			for command_line in command_lines:
				replaceIn_write.write("allCommands.Add(new Command(@\""+command_line+"\"));\n")
				replaceIn_writeDev.write("allCommands.Add(new Command(@\""+command_line+"\"));\n")
		else:
			replaceIn_write.write(line)
			replaceIn_writeDev.write(line)

	replaceIn_write.close()
	replaceIn_writeDev.close()

	os.rename(replaceIn_write_name+".tmp", replaceIn_write_name)
	#print("wrote: "+replaceIn_file)
	os.rename(replaceIn_writeDev_name+".tmp", replaceIn_writeDev_name)
	#print("wrote(Dev): "+replaceIn_file)


def copyWithExtension(l_from, l_to, l_ext):
	l_ext = l_ext.lower()
	for path, dirs, files, in os.walk(l_from):
		for file in files:
			if file.lower().endswith(l_ext):
				target = l_to + path.replace(l_from, '')
				createDir(target)
				shutil.copy2(path + '/' + file, target)
				

print ('\n\n')

if not os.path.exists(buildIni):
	shutil.copy(buildIniTemplate, buildIni)
	sys.exit(0)

exec(open(buildIni).read())

createDir(finalDir)
createDir(finalDirDev)

# erase old data
for file in os.listdir(finalDir):
	path = finalDir + '\\' + file
	if (os.path.isdir(path)):
		eraseDir(path)
for file in os.listdir(finalDirDev):
	path = finalDirDev + '\\' + file
	if (os.path.isdir(path)):
		eraseDir(path)

# get modules
os.chdir(startDir + '/Scripts/')
for file in os.listdir(startDir + '/Scripts/'):
	if file[0] == '.':
		continue
	if os.path.isdir(file):
		modules.append(file)

# copy data, models, and textures
copyWithExtension(startDir + '/Data/', finalDir + '/Data/', '.sbc')
copyWithExtension(startDir + '/Data/', finalDirDev + '/Data/', '.sbc')
copyWithExtension(startDir + '/Models/', finalDir + '/Models/', '.mwm')
copyWithExtension(startDir + '/Models/', finalDirDev + '/Models/', '.mwm')
copyWithExtension(startDir + '/Textures/', finalDir + '/Textures/', '.dds')
copyWithExtension(startDir + '/Textures/', finalDirDev + '/Textures/', '.dds')

# build scripts
createDir(finalScript)
createDir(finalScriptDev)
for module in modules[:]:
	copyScripts(module)

# build help.cs
build_help()

#    Pack Archive

os.chdir(startDir)

for path, dirs, files in os.walk('Archive'):
	size = 0
	for f in files:
		fp = os.path.join(path, f)
		size += os.path.getsize(fp)
if (size < 10000000):
	sys.exit()

if not os.path.exists(Zip7):
	print('\nNot running 7-Zip')
	sys.exit()

print("\n7-Zip running")

cmd = [Zip7, 'u', 'Archive.7z', 'Archive']
process = subprocess.Popen(cmd, stdout = open(os.devnull, 'wb'))
process.wait()

if process.returncode != 0:
	print("\n7-Zip failed\n")
	sys.exit()

print("7-Zip finished\n")

# copied from http://stackoverflow.com/questions/1213706/what-user-do-python-scripts-run-as-in-windows/1214935#1214935
def handleRemoveReadonly(func, path, exc):
	excvalue = exc[1]
	if func in (os.rmdir, os.remove) and excvalue.errno == errno.EACCES:
		os.chmod(path, stat.S_IRWXU| stat.S_IRWXG| stat.S_IRWXO) # 0777
		func(path)
	else:
		raise

shutil.rmtree('Archive', ignore_errors=False, onerror=handleRemoveReadonly)
