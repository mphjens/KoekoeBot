<!DOCTYPE html>
<html lang="en">
	<head>
		<meta charset="UTF-8">
		<title>KoekoePanel</title>
		<style>
			#updatesamplestext {
				background: transparent;
				color: #ebe412;
				border: none;
				width: auto;
				font-size: 19px;
				cursor: pointer;
			}
		</style>
	</head>
	<body style="text-align: center;font-size: 1.3rem;background: rgb(0 0 0);color: rgb(227 227 227);background-image: url('robin.jpg');">
		<h1>KoekoePanel</h1>
		<image width="33%" style="max-width: 512px;" src="achtbaan.gif"/>
		<hr>
		<h2>Upload een sample</h2>
		<form action="koekoeupload.php" method="post" enctype="multipart/form-data">
			Sample (MP3):
				<input type="file" name="the_file" id="fileToUpload" required>
			<br>
			Naampie:
				<input type="text" name="samplename" id="sampleNameField" maxlength="32" required>
			<br>
				<input type="submit" name="submit" value="Alsjeblieft">
		</form>
		<hr>
		<h2>Upload een uur announcement</h2>
		<form action="koekoeupload.php" method="post" enctype="multipart/form-data">
			Announcement (MP3):
				<input type="file" name="the_file" id="fileToUpload" required>
			<br>
			Naampie:
				<input type="text" name="samplename" id="sampleNameField" maxlength="32" required>
			<br>
			Voor:
				<select name="hour" id="hourField" required>
					<option value="1">1 uur</option>
					<option value="2">2 uur</option>
					<option value="3">3 uur</option>
					<option value="4">4 uur</option>
					<option value="5">5 uur</option>
					<option value="6">6 uur</option>
					<option value="7">7 uur</option>
					<option value="8">8 uur</option>
					<option value="9">9 uur</option>
					<option value="10">10 uur</option>
					<option value="11">11 uur</option>
					<option value="12">12 uur</option>
				</select>
					
			<br>
				<input type="submit" name="submit" value="Alsjeblieft">
		</form>
	</body>
</html>
