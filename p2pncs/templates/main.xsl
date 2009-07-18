<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet version="1.0" xmlns="http://www.w3.org/1999/xhtml" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
	<xsl:import href="base.xsl" />

	<xsl:template name="_title">p2pncs</xsl:template>
	<xsl:template name="_css">
		<link type="text/css" rel="stylesheet" href="/css/main.css" />
	</xsl:template>
 
	<xsl:template match="/page">
		<h1>
			<xsl:text>p2pncs </xsl:text>
			<xsl:value-of select="@ver" />
		</h1>
		<p>左のメニューから行いたい操作を選んでください。</p>
		<h2>ネットワークの状態</h2>
		<ul>
			<li>
				<xsl:text>BBS/Wiki用 匿名ネットワーク: </xsl:text>
				<xsl:choose>
					<xsl:when test="/page/network-state/mmlc-mcr = 'Stable'">
						<xsl:text>接続済み</xsl:text>
					</xsl:when>
					<xsl:when test="/page/network-state/mmlc-mcr = 'Unstable'">
						<xsl:text>接続済み (不安定)</xsl:text>
					</xsl:when>
					<xsl:otherwise>
						<xsl:text>接続されていません。</xsl:text>
						<br/>
						<xsl:text>初期ノードからノード情報を追加するか、ルータやファイアウォールの設定を見直してください。</xsl:text>
					</xsl:otherwise>
				</xsl:choose>
			</li>
		</ul>
	</xsl:template>
</xsl:stylesheet>
