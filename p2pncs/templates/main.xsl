<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet version="1.0" xmlns="http://www.w3.org/1999/xhtml" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
	<xsl:import href="base.xsl" />

	<xsl:template name="_title">p2pncs</xsl:template>
	<xsl:template name="_css">
		<link type="text/css" rel="stylesheet" href="/css/main.css" />
	</xsl:template>
 
	<xsl:template match="/page">
		<h1>
			<xsl:text>p2pncs(RinGOch) </xsl:text>
			<xsl:value-of select="@ver" />
		</h1>
		<p>左のメニューから行いたい操作を選んでください。</p>
		<h2>メニューの詳細</h2>
		<ul>
			<li>
				<xsl:text>ネットワーク</xsl:text>
				<dl>
					<dt>初期ノードに接続</dt>
					<dd>
						<p>P2Pネットワークに参加するために、既に参加しているノードの情報を入力して、ネットワークへ参加します。</p>
						<p>起動したら最初に1度だけ実行してください。</p>
					</dd>
					<dt>終了</dt>
					<dd><p>プログラムを終了します。</p></dd>
				</dl>
			</li>
			<li>
				<xsl:text>BBS</xsl:text>
				<dl>
					<dt>新規作成</dt>
					<dd><p>掲示板を新規作成します。</p></dd>
					<dt>開く</dt>
					<dd><p>BBSのIDを指定して掲示板を開きます。</p></dd>
					<dt>一覧</dt>
					<dd><p>キャッシュされた掲示板の一覧を表示します。</p></dd>
				</dl>
			</li>
		</ul>
	</xsl:template>
</xsl:stylesheet>
