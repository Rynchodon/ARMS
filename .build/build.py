# build.py
#
# This script combines the individual module folders into a single structure
# for Space Engineers to load (and a bunch of other useful deploy tasks)
#
# It will create two mods,
#   "%AppData%\SpaceEngineers\Mods\Autopilot" and
#   "%AppData%\SpaceEngineers\Mods\Autopilot Dev".
#
# The Dev version has logging enabled


import datetime, errno, os, os.path, shutil, stat, subprocess, sys, time

# primary directories
scriptDir = os.path.dirname(os.path.realpath(sys.argv[0]))
buildIni = scriptDir + "\\build.ini"
startDir = os.path.split(scriptDir)[0]
appData = os.getenv('APPDATA')
build_model = scriptDir + "\\build-model.py"

# in case build.ini is missing variables
mwmBuilder = os.devnull
Zip7 = os.devnull

def investigateBadPath(s_printName, s_path):
	if s_path is os.devnull:
		print (s_printName + " set to null device")
	else:
		print ("ERROR: incorrect path to " + s_printName)
		lastPath = s_path
		while (not os.path.exists(s_path)):
			if (len(s_path) == 0):
				break
			if (s_path[-1] is "\\"):
				s_path = s_path[:-1]
			lastPath = s_path
			s_path = os.path.dirname(s_path)
		print ("\tbad path:  " + lastPath)
		print ("\tgood path: " + s_path)

if os.path.exists(buildIni):
	exec(open(buildIni).read())
else:
	investigateBadPath("build.ini", buildIni)

modules = []
for file in os.listdir(startDir):
	if file[0] == '.' or file == "Archive":
		continue
	if os.path.isdir(file):
		modules.append(file)

endDir = appData + r"\SpaceEngineers\Mods\Autopilot"
endDirDev = appData + r"\SpaceEngineers\Mods\Autopilot Dev"

# do not change or else log file and settings file will be moved
destScript = endDir + r"\Data\Scripts\Autopilot"
destScriptDev = endDirDev + r"\Data\Scripts\Autopilot"

destData = endDir + r"\Data"
destDataDev = endDirDev + r"\Data"

destModel = endDir + r"\Models\Cubes"
destModelDev = endDirDev + r"\Models\Cubes"

destTextureCube = endDir + r"\Textures\Models\Cubes"
destTextureCubeDev = endDirDev + r"\Textures\Models\Cubes"

destTexturePanel = endDir + r"\Textures\Models"
destTexturePanelDev = endDirDev + r"\Textures\Models"

destTextureIcon = endDir + r"\Textures\GUI\Icons\Cubes"
destTextureIconDev = endDirDev + r"\Textures\GUI\Icons\Cubes"

def createDir(l_dir):
	if not os.path.exists(l_dir):
		#print ("making: "+l_dir)
		os.makedirs(l_dir)

def eraseDir(l_dir):
	if os.path.exists(l_dir):
		#print ("deleting: "+l_dir)
		shutil.rmtree(l_dir)

print("\ncleaning old data")
# erase Data, Models, & Textures
eraseDir(destData)
eraseDir(destDataDev)
eraseDir(destModel)
eraseDir(destModelDev)
eraseDir(endDir + r"\Textures")
eraseDir(endDirDev + r"\Textures")

# create Scripts
createDir(destScript)
createDir(destScriptDev)

# method that takes a module name and moves the files
def copyFiles(l_source):
	print ("copying from "+l_source)
	l_sourceDir = startDir + "\\" + l_source + "\Scripts"
	l_dataDir = startDir + "\\" + l_source + "\Data"
	l_archiveDir = startDir + "\Archive\\" + l_source

	createDir(l_archiveDir)
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
			l_destFile = destScript + "\\" + l_destFileName
			l_destFileDev = destScriptDev + "\\" + l_destFileName

			if ("remove on build" in lines[0]):
				#print ("removing first line" +" in "+file)
				lines[0] = "// line removed by build.py "
				destFile = open(l_destFile, 'w')
				for line in lines:
					destFile.write(line)
				destFile.close()
			else:
				shutil.copy(file, l_destFile)

			shutil.copy(file, l_destFileDev)

			# for archive, add date and time to file name
			d = datetime.datetime.fromtimestamp(os.path.getmtime(file))
			formated = str(d.year)+"-"+str(d.month).zfill(2)+"-"+str(d.day).zfill(2)+"_"+str(d.hour).zfill(2)+"-"+str(d.minute).zfill(2)+"_"+file
			archive = l_archiveDir +"\\"+formated
			try:
				os.chmod(archive, stat.S_IWRITE)
			except OSError:
				pass
			shutil.copyfile(file, archive)
			os.chmod(archive, stat.S_IREAD)

	if os.path.exists(l_dataDir):
		copyWithExtension(l_dataDir, destDataDev, ".sbc")
		copyWithExtension(l_dataDir, destData, ".sbc")

def copyWithExtension(l_from, l_to, l_ext):
	createDir(l_to)
	os.chdir(l_from)
	for file in os.listdir(l_from):
		if file.lower().endswith(l_ext.lower()):
			shutil.copy(file, l_to)

# start mwmBuilder first, it will run in parallel
mwmProcess = []
if os.path.exists(mwmBuilder):
	print("\nrunning MwmBuilder")
	for module in modules[:]:
		modelDir = startDir + "\\" + module + "\\Model\\large"
		if os.path.exists(modelDir):
			os.chdir(modelDir)
			mwmProcess.append(subprocess.Popen(["python", build_model]))
		modelDir = startDir + "\\" + module + "\\Model\\small"
		if os.path.exists(modelDir):
			os.chdir(modelDir)
			mwmProcess.append(subprocess.Popen(["python", build_model]))
		modelDir = startDir + "\\" + module + "\\Models\\large"
		if os.path.exists(modelDir):
			os.chdir(modelDir)
			mwmProcess.append(subprocess.Popen(["python", build_model]))
		modelDir = startDir + "\\" + module + "\\Models\\small"
		if os.path.exists(modelDir):
			os.chdir(modelDir)
			mwmProcess.append(subprocess.Popen(["python", build_model]))
else:
	investigateBadPath("MwmBuilder", mwmBuilder)

# copy textures
for module in modules[:]:
	textureDir = startDir + "\\" + module + "\\Textures\\Cubes"
	if os.path.exists(textureDir):
		copyWithExtension(textureDir, destTextureCube, ".dds")
		copyWithExtension(textureDir, destTextureCubeDev, ".dds")
	textureDir = startDir + "\\" + module + "\\Textures\\TextPanel"
	if os.path.exists(textureDir):
		copyWithExtension(textureDir, destTexturePanel, ".dds")
		copyWithExtension(textureDir, destTexturePanelDev, ".dds")
	textureDir = startDir + "\\" + module + "\\Textures\\Icon"
	if os.path.exists(textureDir):
		copyWithExtension(textureDir, destTextureIcon, ".dds")
		copyWithExtension(textureDir, destTextureIconDev, ".dds")

# copy scripts, data
for module in modules[:]:
	copyFiles(module)

#	start of build Help.cs
replaceIn_file=r"Help.cs"
replaceIn_DirSource= startDir + r"\Autopilot\Scripts"

readme_file= startDir + r"\Player Readme.txt"

replaceIn_write_name = destScript+"\\Autopilot."+replaceIn_file
replaceIn_writeDev_name = destScriptDev+"\\Autopilot."+replaceIn_file

replaceIn_read = open(replaceIn_DirSource+"\\"+replaceIn_file, 'r')
replaceIn_write = open(replaceIn_write_name+".tmp", 'w')
replaceIn_writeDev = open(replaceIn_writeDev_name+".tmp", 'w')
readme_read = open(readme_file, 'r')

#	get all commands
readme_lines = readme_read.readlines()
command_lines = []
command_current = ""
index = 0
position = 0
while (position == 0):
	if ("Commands" in readme_lines[index]):
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

#	copy all commands
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
#	end of build Help.cs
print ("\nfinished .cs and .sbc\n")


# wait on mwmBuilder
if os.path.exists(mwmBuilder):
	for process in mwmProcess[:]:
		process.wait()

	print("\nfinished MwmBuilder\n")

# copy mwm files
for module in modules[:]:
	# large models
	modelDir = startDir + "\\" + module + "\\Model\\large"
	if os.path.exists(modelDir):
		copyWithExtension(modelDir, destModel + "\\large", ".mwm")
		copyWithExtension(modelDir, destModelDev + "\\large", ".mwm")
	# small models
	modelDir = startDir + "\\" + module + "\\Model\\small"
	if os.path.exists(modelDir):
		copyWithExtension(modelDir, destModel + "\\small", ".mwm")
		copyWithExtension(modelDir, destModelDev + "\\small", ".mwm")
	# large models
	modelDir = startDir + "\\" + module + "\\Models\\large"
	if os.path.exists(modelDir):
		copyWithExtension(modelDir, destModel + "\\large", ".mwm")
		copyWithExtension(modelDir, destModelDev + "\\large", ".mwm")
	# small models
	modelDir = startDir + "\\" + module + "\\Models\\small"
	if os.path.exists(modelDir):
		copyWithExtension(modelDir, destModel + "\\small", ".mwm")
		copyWithExtension(modelDir, destModelDev + "\\small", ".mwm")

print("\nfinished build\n")

#	Pack Archive

os.chdir(startDir)

if not os.path.exists(Zip7):
	investigateBadPath("7-Zip", Zip7)
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
