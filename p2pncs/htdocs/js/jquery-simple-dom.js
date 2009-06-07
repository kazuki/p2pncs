/*
 * Simple DOM Utilities for jQuery
 * Copyright(C) 2009 Kazuki Oikawa
 * Dual licensed under the MIT and GPL licenses.
 *
 * Example:
 *   $.create('div',{style:'font-weight:bold'},[
 *     'div',{},['Hello World','br',{},'Test Message'],
 *     'div',{},[
 *       'ul',{},[
 *         'li',{},['Item 1'],
 *         'li',{},['Item 2'],
 *         'li',{},['Item 3']
 *       ]
 *     ]
 *   ]);
 *
 *  output:
 *    <div style="font-weight:bold">
 *      <div>Hello World<br/>Test Message</div>
 *      <div>
 *        <ul>
 *          <li>Item 1</li>
 *          <li>Item 2</li>
 *          <li>Item 3</li>
 *        </ul>
 *      </div>
 *    </div>
*/
jQuery.extend({
	createText: function(txt) {
		return jQuery(document.createTextNode(txt));
	},
	create: function() {
		function isString (obj) {
			return typeof(obj) == "string" || obj instanceof String;
		}
		var ret = [];
		var args = arguments;
		if (!jQuery.isArray(args))
			args = jQuery.makeArray(args);
		if (args.length == 1 && jQuery.isArray(args[0]))
			args = args[0];
		if (args.length == 0)
			return ret;
		if (args.length == 1 || isString(args[1])) {
			ret.push (jQuery.createText(args[0]));
			if (args.length > 1) {
				var tmp = jQuery.create (args.slice(1));
				for (var i = 0; i < tmp.length; i ++)
					ret.push (tmp[i]);
			}
		} else {
			var element = jQuery (document.createElement (args[0]));
			for (var attr in args[1])
				element.attr (attr, args[1][attr]);
			ret.push (element);
			var idx = 3;
			if (args.length > 2) {
				if (jQuery.isArray(args[2])) {
					var tmp = jQuery.create(args[2]);
					for (var i = 0; i < tmp.length; i ++)
						element.append (tmp[i]);
				} else {
					idx = 2;
				}
			}
			if (args.length >= idx) {
				tmp = jQuery.create(args.slice(idx));
				for (var i = 0; i < tmp.length; i ++)
					ret.push (tmp[i]);
			}
		}
		if (arguments.length == 3)
			return ret[0];
		return ret;
	}
});
