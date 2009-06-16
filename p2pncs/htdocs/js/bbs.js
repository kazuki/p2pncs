$(function() {
	$("#postButton").click(function() {
		var postName = encodeURIComponent ($("#postName").val());
		var postBody = encodeURIComponent ($("#postBody").val());
		var bbsKey = $("#postKey").val();
		var postUrl = "/bbs/" + bbsKey + "?name=" + postName + "&body=" + postBody;
		$.ajax({
			type: "POST",
			url: postUrl,
			async: false
		});
		$("#postBody").val("");
		window.location.reload ();
	});
});
