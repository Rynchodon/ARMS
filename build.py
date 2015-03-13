#	will create two mods %AppData%\SpaceEngineers\Mods\Autopilot" and %AppData%\SpaceEngineers\Mods\Autopilot Dev"
#	the Dev version has logging enabled


import datetime, errno, os, os.path, psutil, shutil, stat, subprocess, sys, time

# in case build.ini is missing variables
mwmBuilder = os.devnull
Zip7 = os.devnull

exec(open('build.ini').read())

startDir = os.path.dirname(os.path.realpath(sys.argv[0]))
appData = os.getenv('APPDATA')

print (appData)

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

# method that takes a name and moves the files
def copyScriptFiles(l_source):
	print ("copying from "+l_source)
	l_sourceDir = startDir + "\\" + l_source + "\Scripts"
	l_dataDir = startDir + "\\" + l_source + "\Data"
	l_archiveDir = startDir + "\Archive\\" + l_source
	
	createDir(l_archiveDir)
	
	os.chdir(l_sourceDir)
	for file in os.listdir(l_sourceDir):
		if file.endswith(".cs"):
			#print ("file is "+file)
			lines = open(file, 'r').readlines()
			if ("skip file on build" in lines[0]):
				#print ("skipping "+file)
				continue
			
			l_destFile = destScript + "\\" + l_source + '.' + file
			l_destFileDev = destScriptDev + "\\" + l_source + '.' + file

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
		if file.endswith(l_ext):
			shutil.copy(file, l_to)

def emptyDirectory(l_startDir, l_current):
	if not os.path.exists(l_current):
		return
	for item in os.listdir(l_current):
		fullPath = l_current + "\\" + item
		if os.path.isdir(fullPath):
			print("descending into: " + item)
			emptyDirectory(l_startDir, fullPath)
		else:
			print("moving file up: " + item)
			fullDest = l_startDir + "\\" + item
			if os.path.exists(fullDest):
				os.remove(fullDest)
			shutil.copy(fullPath, fullDest)

# start mwmBuilder first, it will run in parallel
#	not working ATM
sourceModelRadarLarge = startDir + r"\AntennaRelay\Model\large"
sourceModelRadarSmall = startDir + r"\AntennaRelay\Model\small"

if os.path.exists(mwmBuilder):
	print("\nrunning mwmBuilder")
	os.chdir(sourceModelRadarLarge)
	createDir('ouput')
	mwmLarge = subprocess.Popen([mwmBuilder, "/s:.", "/o:./output/"])
	os.chdir(sourceModelRadarSmall)
	createDir('ouput')
	mwmSmall = subprocess.Popen([mwmBuilder, "/s:.", "/o:./output/"])

# copy textures
copyWithExtension(startDir + r"\AntennaRelay\Model\Textures\Cubes", destTextureCube, ".dds")
copyWithExtension(startDir + r"\AntennaRelay\Model\Textures\Cubes", destTextureCubeDev, ".dds")
copyWithExtension(startDir + r"\AntennaRelay\Model\Textures\TextPanel", destTexturePanel, ".dds")
copyWithExtension(startDir + r"\AntennaRelay\Model\Textures\TextPanel", destTexturePanelDev, ".dds")
copyWithExtension(startDir + r"\AntennaRelay\Model\Textures\Icon", destTextureIcon, ".dds")
copyWithExtension(startDir + r"\AntennaRelay\Model\Textures\Icon", destTextureIconDev, ".dds")

# copy scripts
copyScriptFiles("Autopilot")
copyScriptFiles("Utility")
copyScriptFiles("AntennaRelay")
copyScriptFiles("TurretControl")

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

if os.path.exists(mwmBuilder):
	# wait on mwmBuilder
	mwmLarge.wait()
	mwmSmall.wait()
	print("\nfinished MwmBuilder\n")

emptyDirectory(sourceModelRadarLarge, sourceModelRadarLarge + "\\output")
emptyDirectory(sourceModelRadarSmall, sourceModelRadarSmall + "\\output")
copyWithExtension(sourceModelRadarLarge, destModel + "\\" + "large", ".mwm")
copyWithExtension(sourceModelRadarSmall, destModel + "\\" + "small", ".mwm")
copyWithExtension(sourceModelRadarLarge, destModelDev + "\\" + "large", ".mwm")
copyWithExtension(sourceModelRadarSmall, destModelDev + "\\" + "small", ".mwm")


print("\nfinished build\n")

#	Pack Archive

os.chdir(startDir)

if not os.path.exists(Zip7):
	print ("\n7-Zip not found\n")
	sys.exit()

print("\n7-Zip running")

cmd = [Zip7, 'u', 'Archive.7z', 'Archive']
process = subprocess.Popen(cmd, stdout = open(os.devnull, 'wb'))
process.wait()

if process.returncode != 0:
	print("\n7-Zip failed\n")
	sys.exit()

print("7-Zip finished\n")

#	http://stackoverflow.com/questions/1213706/what-user-do-python-scripts-run-as-in-windows/1214935#1214935
def handleRemoveReadonly(func, path, exc):
  excvalue = exc[1]
  if func in (os.rmdir, os.remove) and excvalue.errno == errno.EACCES:
      os.chmod(path, stat.S_IRWXU| stat.S_IRWXG| stat.S_IRWXO) # 0777
      func(path)
  else:
      raise

shutil.rmtree('Archive', ignore_errors=False, onerror=handleRemoveReadonly)