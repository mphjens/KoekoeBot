<html>
	<head>
		<title>KoekoePanel</title>
		<style>
			#updatesamplestext {
				background: transparent;
				color: #ebe412;
				border: none;
				width: auto;
				font-size: 19px;
				cursor: pointer;
				width: 170px;
			}
		</style>
	<head>
	<body style="text-align: center; font-size: 1.3rem;background: rgb(0 0 0);color: rgb(227 227 227);">
	<script>
			function copyText() {
			  var textbox = document.getElementById("updatesamplestext");
			  textbox.select();
			  textbox.setSelectionRange(0, textbox.value.length); // For mobile devices
			  document.execCommand('copy')
			  alert("Copied to clipboard: " + textbox.value);
			}
	</script>
	<?php
		include('mp3file.class.php');
		error_reporting(E_ALL & ~E_NOTICE);

		$errors = []; // Store errors here

		$fileExtensionsAllowed = ['mp3']; // These will be the only file extensions allowed 

		if (isset($_POST['submit']) && isset($_FILES['the_file'])) {
			$fileName = $_FILES['the_file']['name'];
			$fileSize = $_FILES['the_file']['size'];
			$fileTmpName  = $_FILES['the_file']['tmp_name'];
			$fileType = $_FILES['the_file']['type'];
			$fileExtension = strtolower(end(explode('.',$fileName)));
			
			$isHourAnnouncement = isset($_POST['hour']);
			$hour = intval($_POST['hour']);
			
			$sampleName = $_POST['samplename'];
			$sampleName = str_replace(array('\\','/',':','*','?','"','<','>','|','.'),'',$sampleName);
			
			$prefix = "extra_";
			if($isHourAnnouncement && $hour > 0 && $hour <= 12)
				$prefix = $_POST['hour']."_uur_";
			
			$uploadPath = "/home/jons/koekoe-dist/samples/". basename($prefix.str_replace(' ', '_', $sampleName).".mp3"); 
			if(file_exists($uploadPath)) {
				echo "<image width='33%' style='max-width: 512px;' src='bassie-and-adriaan-schrikken.gif' style='margin-bottom: 10px;'/><br>";
				if($isHourAnnouncement) {
					echo "<strong>".$hour." uur </strong> voor <strong>$sampleName</strong> bestaat al, kies een andere naam..";
				} else {
					echo "<strong>".$sampleName."</strong> bestaat al, kies een andere naam..";
				}
				exit();
			}
			if (strlen($sampleName) >= 3 && strlen($sampleName) < 32) {

			  if (! in_array($fileExtension,$fileExtensionsAllowed)) {
				$errors[] = "Alleen mp3 files.";
			  }
			
				$mp3file = new MP3File($fileTmpName);
				$duration = $mp3file->getDuration();//(slower) for VBR (or CBR)

			  if ($fileSize > 4000000 || $duration > 10) {
				$errors[] = "Te groot of te lang (10 seconden of 3MB)";
			  }

			  if (empty($errors)) {
				$didUpload = move_uploaded_file($fileTmpName, $uploadPath);

				if ($didUpload) {
				  echo "<image width='33%' style='max-width: 512px;' src='adriaan-lachen.gif' style='margin-bottom: 10px;'/><br>";
				  if($isHourAnnouncement) {
					  echo "Ik heb een announcement voor <strong>" . $hour . " uur </strong> toegevoegd";
				  } else {
					  echo "Ik heb <strong>" . $sampleName . "</strong> voor je toegevoegd<br> Vergeet niet <br><strong><input onclick='copyText()' type='text' value='!kk updatesamples' id='updatesamplestext'></strong><br> te doen";
				  }
				  exit();
				} else {
					echo "<image width='33%' style='max-width: 512px;' src='bassie-and-adriaan-schrikken.gif' style='margin-bottom: 10px;'/><br>";
				  echo "Kapot.";
				}
			  } else {
				  echo "<image width='33%' style='max-width: 512px;' src='bassie-and-adriaan-schrikken.gif' style='margin-bottom: 10px;'/><br>";
				foreach ($errors as $error) {
				  echo $error . "\n";
				}
			  }	
			} else {
				echo "<image width='33%' style='max-width: 512px;' src='bassie-and-adriaan-schrikken.gif' style='margin-bottom: 10px;'/><br>";
				echo "Sample naam moet 3 tekens lang zijn";
				exit();
			}
		}
		else {
			echo "No file found";
		}
	?>
	</body>
</html>

