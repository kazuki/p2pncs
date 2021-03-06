<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet version="1.0" xmlns="http://www.w3.org/1999/xhtml" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
	<xsl:output method="xml" encoding="utf-8" doctype-public="-//W3C//DTD XHTML 1.1//EN" doctype-system="http://www.w3.org/TR/xhtml11/DTD/xhtml11.dtd" />

	<xsl:template match="/">
		<html>
			<head>
				<title>
					<xsl:call-template name="_title" />
				</title>
				<link type="text/css" rel="stylesheet" href="/css/base.css" />
				<xsl:call-template name="_css" />
				<xsl:call-template name="_js" />
			</head>
			<body>
				<div id="header" />
				<div id="menu">
					<ul>
						<li><a href="/">トップページ</a></li>
						<li>
							<xsl:text>ネットワーク</xsl:text>
							<ul>
								<li><a href="/net/init">初期ノード</a></li>
								<li><a href="/statistics">統計情報</a></li>
								<li><a href="/net/exit">終了</a></li>
							</ul>
						</li>
						<li>
							<a href="/manage/">管理</a>
							<ul>
								<li><a href="/bbs/new">掲示板を新規作成</a></li>
								<li><a href="/wiki/new">Wikiを新規作成</a></li>
							</ul>
						</li>
						<li><a href="/open">IDを指定してファイルを開く</a></li>
						<li>
							<xsl:text>ファイル一覧</xsl:text>
							<ul>
								<li><a href="/list">キャッシュ済み</a></li>
								<li><a href="/list?empty">全て</a></li>
							</ul>
						</li>
					</ul>
				</div>
				<div id="contents">
					<xsl:apply-templates select="*" />
				</div>
				<div id="footer" />
			</body>
		</html>
	</xsl:template>

	<xsl:template name="_title">
		<xsl:text>unknown page :: p2pncs</xsl:text>
	</xsl:template>
	<xsl:template name="_css" />
	<xsl:template name="_js" />

</xsl:stylesheet>