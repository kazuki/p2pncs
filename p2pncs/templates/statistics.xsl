<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet version="1.0" xmlns="http://www.w3.org/1999/xhtml" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
	<xsl:import href="base.xsl" />

	<xsl:template name="_title">Statistics :: p2pncs</xsl:template>
	<xsl:template name="_css">
		<link type="text/css" rel="stylesheet" href="/css/statistics.css" />
	</xsl:template>
 
	<xsl:template match="/page">
		<h1>統計情報</h1>
		<xsl:apply-templates select="statistics/traffic" />
		<xsl:apply-templates select="statistics/messaging" />
	</xsl:template>

	<xsl:template match="traffic">
		<h2>トラフィック</h2>
		<table>
			<thead>
				<tr>
					<td />
					<td>平均</td>
					<td>合計</td>
				</tr>
			</thead>
			<tbody>
				<tr>
					<th>受信バイト数: </th>
					<td><xsl:value-of select="floor(average/@recv-bytes * 8 div 100) div 10" />kbps</td>
					<td><xsl:value-of select="total/@recv-bytes" /></td>
				</tr>
				<tr>
					<th>受信パケット数: </th>
					<td><xsl:value-of select="floor(average/@recv-packets)" />pps</td>
					<td><xsl:value-of select="total/@recv-packets" /></td>
				</tr>
				<tr>
					<th>送信バイト数: </th>
					<td><xsl:value-of select="floor(average/@send-bytes * 8 div 100) div 10" />kbps</td>
					<td><xsl:value-of select="total/@send-bytes" /></td>
				</tr>
				<tr>
					<th>送信パケット数: </th>
					<td><xsl:value-of select="floor(average/@send-packets)" />pps</td>
					<td><xsl:value-of select="total/@send-packets" /></td>
				</tr>
			</tbody>
		</table>
	</xsl:template>

	<xsl:template match="messaging">
		<h2>メッセージング</h2>
		<table>
			<thead>
				<tr>
					<td>Index</td>
					<td>RTT-Avg</td>
					<td>RTT-SD</td>
					<td>Success</td>
					<td>Failure</td>
					<td>Retries</td>
				</tr>
			</thead>
			<tbody>
				<xsl:for-each select="entry">
					<tr>
						<td><xsl:value-of select="position()" /></td>
						<td><xsl:value-of select="floor(@rtt-avg)" />ms</td>
						<td>±<xsl:value-of select="floor(@rtt-sd)" />ms</td>
						<td><xsl:value-of select="@success" /></td>
						<td><xsl:value-of select="@fail" /></td>
						<td><xsl:value-of select="@retries" /></td>
					</tr>
				</xsl:for-each>
			</tbody>
		</table>
	</xsl:template>
</xsl:stylesheet>
