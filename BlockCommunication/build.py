import datetime, os, os.path, shutil, stat, time

sourceDir=os.getcwd()+r"\Scripts"
destDir=r"C:\Users\Alexander Durand\AppData\Roaming\SpaceEngineers\Mods\BlockCommunication\Data\Scripts\BlockCommunication"
destDirDev=r"C:\Users\Alexander Durand\AppData\Roaming\SpaceEngineers\Mods\BlockCommunication Dev\Data\Scripts\BlockCommunication"
archiveDir=sourceDir+r"\Archive"


# create directories
if not os.path.exists(destDir):
	os.makedirs(destDir)
if not os.path.exists(destDirDev):
	os.makedirs(destDirDev)
if not os.path.exists(archiveDir):
	os.makedirs(archiveDir)

# clean destDir directory
os.chdir(destDir)
for file in os.listdir(destDir):
	if file.endswith(".cs"):
		print("removing "+file)
		os.remove(file)

# clean destDirDev directory
os.chdir(destDirDev)
for file in os.listdir(destDirDev):
	if file.endswith(".cs"):
		print("removing "+file)
		os.remove(file)

os.chdir(sourceDir)
for file in os.listdir(sourceDir):
	if file.endswith(".cs"):
		lines = open(file, 'r').readlines()
		if ("skip file on build" in lines[0]):
			print ("skipping "+file)
			continue
		
		if ("remove on build" in lines[0]):
			print ("removing first line" +" in "+file)
			lines[0] = "// line removed by build.py "
			destFile = open(destDir+"\\"+file, 'w')
			for line in lines:
				destFile.write(line)
			destFile.close()
		else:
			shutil.copy(file, destDir)

		shutil.copy(file, destDirDev)

		# for archive, add date and time to file name
		d = datetime.datetime.fromtimestamp(os.path.getmtime(file))
		formated = str(d.year)+"-"+str(d.month).zfill(2)+"-"+str(d.day).zfill(2)+"_"+str(d.hour).zfill(2)+"-"+str(d.minute).zfill(2)+"_"+file
		archive = archiveDir+"\\"+formated
		try:
			os.chmod(archive, stat.S_IWRITE)
		except OSError:
			pass
		shutil.copyfile(file, archive)
		os.chmod(archive, stat.S_IREAD)


print("finished build\n")
exec(open(r"C:\Users\Alexander Durand\Desktop\Games\Space Engineers\Space Engineers.py").read())