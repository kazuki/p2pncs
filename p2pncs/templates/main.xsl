<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet version="1.0" xmlns="http://www.w3.org/1999/xhtml" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
	<xsl:output method="xml" encoding="utf-8" doctype-public="-//W3C//DTD XHTML 1.1//EN" doctype-system="http://www.w3.org/TR/xhtml11/DTD/xhtml11.dtd" />

	<xsl:template match="/">
		<html>
			<head>
				<title>p2pncs</title>
				<link type="text/css" rel="stylesheet" href="css/ui-lightness/jquery-ui-1.7.2.custom.css" />
				<link type="text/css" rel="stylesheet" href="css/main.css" />
				<script type="text/javascript" charset="utf-8" src="js/jquery-1.3.2.min.js" />
				<script type="text/javascript" charset="utf-8" src="js/jquery-ui-1.7.2.custom.min.js" />
				<script type="text/javascript" charset="utf-8" src="js/jquery-simple-dom.js" />
				<script type="text/javascript" charset="utf-8" src="js/main.js" />
			</head>
			<body>
				<div id="container">
					<div id="header">
						<h1>p2pncs:</h1>
					</div>
					<div id="sidearea">
						<div id="navigation">
							<h3><a href="#">Network <span id="mainStatus" /></a></h3>
							<div>
								<ul class="no-indent">
									<li><a id="connect_dialog" href="#">初期ノードに接続</a></li>
									<li><a id="exit_dialog" href="#">プログラムを終了</a></li>
									<li><a id="throughput_test_dialog" href="#">スループット測定 (危険!)</a></li>
								</ul>
							</div>
							<h3><a href="#">Chat</a></h3>
							<div>
								<ul class="no-indent">
									<li><a id="create_room_dialog" href="#">部屋を作成</a></li>
									<li><a id="join_dialog" href="#">部屋に接続</a></li>
								</ul>
							</div>
						</div>
					</div>
					<div id="content">
						<h1>Welcome...</h1>
						<div>こんにちは<xsl:value-of select="/page/name" />さん。<br/>あなたのIDは<xsl:value-of select="/page/key" />です。</div>
					</div>
				</div>
			</body>
		</html>
	</xsl:template>
</xsl:stylesheet>
